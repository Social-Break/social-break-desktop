using System.Runtime.InteropServices;

namespace SocialBreakTray.Ui;

/// <summary>
/// A window with its own caption bar instead of the OS one.
///
/// Windows draws the system title bar in the *light* theme regardless of
/// the client area behind it, so a dark app gets a bright white strip
/// bolted to its top edge - the single loudest "this is an unstyled Win32
/// dialog" signal a window can give off. DWM's dark-mode attribute fixes
/// the color but not the proportions, and only on Windows 10 1809+.
/// Drawing the caption ourselves gets the same result on every supported
/// Windows version and lets the caption share the window's own header
/// gradient, the way Spotify/VS Code/Discord all do.
///
/// What we give up by going borderless, and how it's covered:
///   - the system frame: replaced by the form's own 1px painted edge
///     (BackColor showing through around the inset caption/content), plus
///     Win11's DWM border color where that's supported;
///   - the drop shadow: restored via CS_DROPSHADOW in CreateParams;
///   - dragging by the title bar: restored by handing the drag back to the
///     OS with WM_NCLBUTTONDOWN/HTCAPTION, so snapping and multi-monitor
///     behaviour stay native rather than being re-implemented badly with
///     mouse deltas.
///
/// Subclasses add their controls to <see cref="Content"/> (not to
/// Controls) and size the window with <see cref="SetContentSize"/>, so
/// their coordinates stay relative to the area below the caption and don't
/// need to know the caption's height.
/// </summary>
internal class DarkForm : Form
{
    protected const int CaptionHeight = 34;
    private const int FrameThickness = 1;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;
    private const int CS_DROPSHADOW = 0x00020000;

    private readonly CaptionBar _caption;

    /// <summary>The area below the caption bar. Everything a subclass shows
    /// goes in here.</summary>
    protected Panel Content { get; }

    protected DarkForm(string title, bool showMinimize)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        // Nothing else paints the outermost pixel ring, so the form's own
        // background is the window's border.
        BackColor = Theme.Border;
        Font = Theme.UiFont;
        Icon = Theme.AppIcon;
        DoubleBuffered = true;

        Content = new Panel { BackColor = Theme.Bg };
        _caption = new CaptionBar(this, title, showMinimize);

        // Positioned explicitly in LayoutChrome rather than docked: Dock
        // resolves against z-order, which is easy to get subtly backwards
        // and leaves the caption overlapping the content by 34px with no
        // obvious cause. Both windows here are fixed-size anyway.
        Controls.Add(Content);
        Controls.Add(_caption);
        LayoutChrome();

        Theme.ApplyBorderlessChrome(this);
    }

    /// <summary>Sizes the window so <see cref="Content"/> ends up exactly
    /// <paramref name="width"/> x <paramref name="height"/>, accounting for
    /// the caption and frame.</summary>
    protected void SetContentSize(int width, int height) =>
        ClientSize = new Size(
            width + FrameThickness * 2,
            height + CaptionHeight + FrameThickness);

    protected override CreateParams CreateParams
    {
        get
        {
            // A borderless window gets no shadow, which makes it look
            // pasted onto the desktop rather than floating above it. This
            // is the standard class style for restoring one.
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChrome();
    }

    private void LayoutChrome()
    {
        int inner = Math.Max(0, ClientSize.Width - FrameThickness * 2);
        _caption.SetBounds(FrameThickness, FrameThickness, inner, CaptionHeight);
        Content.SetBounds(
            FrameThickness,
            FrameThickness + CaptionHeight,
            inner,
            Math.Max(0, ClientSize.Height - CaptionHeight - FrameThickness * 2));
    }

    /// <summary>Paints the caption's background. Subclasses that continue
    /// the same treatment into a banner below the caption override this so
    /// the two line up; the default is the shared header gradient.</summary>
    protected internal virtual void PaintCaptionBackground(Graphics g, Rectangle bounds)
    {
        using var brush = Theme.CreateHeaderBrush(bounds);
        g.FillRectangle(brush, bounds);
    }

    /// <summary>Hands an in-progress drag on the caption back to the window
    /// manager, so the window moves with real OS snapping/animation rather
    /// than a hand-rolled mouse-delta loop.</summary>
    private void BeginDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    /// <summary>The caption strip: app icon, window title, and whichever of
    /// the minimize/close buttons this window supports.</summary>
    private sealed class CaptionBar : Control
    {
        private readonly DarkForm _owner;
        private readonly string _title;

        public CaptionBar(DarkForm owner, string title, bool showMinimize)
        {
            _owner = owner;
            _title = title;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            var close = new CaptionButton(this, CaptionGlyph.Close, Theme.CloseHover, owner.Close);
            Controls.Add(close);

            CaptionButton? minimize = null;
            if (showMinimize)
            {
                minimize = new CaptionButton(this, CaptionGlyph.Minimize, Theme.CaptionHover,
                    () => owner.WindowState = FormWindowState.Minimized);
                Controls.Add(minimize);
            }

            Resize += (_, _) =>
            {
                close.SetBounds(Width - CaptionButton.ButtonWidth, 0, CaptionButton.ButtonWidth, Height);
                minimize?.SetBounds(Width - CaptionButton.ButtonWidth * 2, 0, CaptionButton.ButtonWidth, Height);
            };

            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) owner.BeginDrag();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintBar(e.Graphics);

            int textLeft = 12;
            if (Theme.AppIcon != null)
            {
                // 16px: the icon file has a real 16x16 frame, so this is
                // drawn native rather than scaled down from a larger one.
                Theme.DrawAppIcon(e.Graphics, new Rectangle(11, (Height - 16) / 2, 16, 16));
                textLeft = 35;
            }

            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(
                e.Graphics, _title, Theme.UiFont,
                new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft - CaptionButton.ButtonWidth * 2), Height),
                Theme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        /// <summary>Paints this bar's full-width background. Child buttons
        /// call it through a translated Graphics instead of using a
        /// transparent BackColor, because transparency over an owner-drawn
        /// gradient parent is unreliable and shows as black boxes when it
        /// misfires.</summary>
        public void PaintBar(Graphics g) =>
            _owner.PaintCaptionBackground(g, new Rectangle(0, 0, Width, Height));
    }

    private enum CaptionGlyph { Minimize, Close }

    /// <summary>A caption button. The glyphs are drawn as primitives rather
    /// than set in Segoe MDL2 Assets, so there's no font to be missing and
    /// no chance of rendering a wrong-codepoint box on a stripped-down or
    /// non-English Windows install.</summary>
    private sealed class CaptionButton : Control
    {
        public const int ButtonWidth = 46;

        private readonly CaptionBar _bar;
        private readonly CaptionGlyph _glyph;
        private readonly Color _hoverColor;
        private bool _hovered;

        public CaptionButton(CaptionBar bar, CaptionGlyph glyph, Color hoverColor, Action onClick)
        {
            _bar = bar;
            _glyph = glyph;
            _hoverColor = hoverColor;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            // Not focusable: tabbing into a window's close button ahead of
            // its actual content is never what anyone wants.
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Default;

            MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
            Click += (_, _) => onClick();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Redraw the slice of the parent gradient sitting behind us,
            // shifted into our own coordinate space, so the button is
            // invisible until hovered.
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(-Left, -Top);
            _bar.PaintBar(e.Graphics);
            e.Graphics.Restore(state);

            if (_hovered)
            {
                using var hover = new SolidBrush(_hoverColor);
                e.Graphics.FillRectangle(hover, ClientRectangle);
            }

            var glyphColor = _hovered && _glyph == CaptionGlyph.Close ? Color.White : Theme.TextSecondary;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(glyphColor, 1.2f);

            int cx = Width / 2;
            int cy = Height / 2;
            if (_glyph == CaptionGlyph.Minimize)
            {
                e.Graphics.DrawLine(pen, cx - 5, cy, cx + 5, cy);
            }
            else
            {
                e.Graphics.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                e.Graphics.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
            }
        }
    }
}
