using System.Drawing.Drawing2D;

namespace SocialBreakTray.Ui;

/// <summary>
/// An owner-drawn checkbox.
///
/// A stock CheckBox draws its glyph from the system visual style, which is
/// a light-themed bitmap - it lands on a dark form as a bright, obviously
/// foreign square, and neither BackColor nor FlatStyle.Flat reaches it
/// (Flat just swaps one hardcoded light treatment for another). Painting
/// the box ourselves is the only way to get it onto the app's palette.
///
/// Sized explicitly by the caller rather than via AutoSize: AutoSize
/// measures through the base class's glyph-aware layout, which no longer
/// matches what's actually drawn here.
/// </summary>
internal sealed class DarkCheckBox : CheckBox
{
    private const int BoxSize = 15;
    private const int TextGap = 9;

    private bool _hovered;

    public DarkCheckBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Bg;
        ForeColor = Theme.TextSecondary;
        Font = Theme.UiFont;
        Cursor = Cursors.Hand;

        MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        CheckedChanged += (_, _) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var box = new Rectangle(0, (Height - BoxSize) / 2, BoxSize, BoxSize);
        using (var path = Theme.RoundedRect(box, 3))
        {
            using var fill = new SolidBrush(Checked ? Theme.AccentGreen : Theme.Surface);
            e.Graphics.FillPath(fill, path);
            using var edge = new Pen(Checked ? Theme.AccentGreen : _hovered ? Theme.TextMuted : Theme.Border);
            e.Graphics.DrawPath(edge, path);
        }

        if (Checked)
        {
            // Tick drawn in the background color so it reads as cut out of
            // the filled box, rather than as a third color on top of it.
            using var tick = new Pen(Theme.Bg, 1.9f);
            int x = box.Left + 3;
            int y = box.Top + box.Height / 2;
            e.Graphics.DrawLines(tick, new[]
            {
                new Point(x, y),
                new Point(x + 3, y + 3),
                new Point(x + 9, y - 4),
            });
        }

        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        int textLeft = BoxSize + TextGap;
        TextRenderer.DrawText(
            e.Graphics, Text, Font,
            new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height),
            _hovered ? Theme.TextPrimary : ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}
