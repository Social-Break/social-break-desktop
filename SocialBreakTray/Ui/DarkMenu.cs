using System.Drawing.Drawing2D;

namespace SocialBreakTray.Ui;

/// <summary>
/// Makes a ContextMenuStrip match the rest of the app.
///
/// WinForms menus render with the Office-2003-era "professional" theme -
/// a white surface, a lighter gutter strip down the left, blue gradient
/// hover, and 3D-etched separators. Against a dark tray app that reads as
/// a completely different program's menu, and it's the one piece of this
/// UI a user sees every single time they interact with the tray icon.
///
/// The color table covers the fills the base renderer looks up; the
/// renderer overrides cover the parts it draws with hardcoded logic
/// (text, separators, the check mark, the outer border) that a color table
/// alone can't reach.
/// </summary>
internal static class DarkMenu
{
    /// <summary>Applies the dark theme to a menu, in place.</summary>
    public static ContextMenuStrip Apply(ContextMenuStrip menu)
    {
        menu.Renderer = new DarkMenuRenderer();
        menu.BackColor = Theme.Surface;
        menu.ForeColor = Theme.TextPrimary;
        menu.Font = Theme.UiFont;
        // The image gutter is dead space here - no item has an icon - but
        // the check margin has to stay for "Start with Windows".
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.Padding = new Padding(0, 5, 0, 5);
        return menu;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            // Rounded per-item edges are part of the same dated look the
            // rest of this class is undoing.
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Theme.Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Theme.Border);
            var bounds = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Deliberately flat: the base renderer paints a gradient strip
            // here, which is exactly the visible "gutter" that makes a
            // themed menu look half-finished.
            using var brush = new SolidBrush(Theme.Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            // Disabled items (the status line at the top) must never look
            // clickable, so they get no hover fill at all.
            if (!e.Item.Selected || !e.Item.Enabled)
            {
                using var brush = new SolidBrush(Theme.Surface);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
                return;
            }

            var rect = new Rectangle(3, 0, e.Item.Width - 6, e.Item.Height);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var hover = new SolidBrush(Theme.MenuHover);
            using var path = Theme.RoundedRect(rect, 4);
            e.Graphics.FillPath(hover, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled)
            {
                // Drawn directly rather than through base, which routes
                // disabled text via ControlPaint's embossed grey-on-grey
                // treatment - illegible on a dark surface.
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, Theme.TextMuted, e.TextFormat);
                return;
            }

            e.TextColor = Theme.TextPrimary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // A hand-drawn tick in the accent color, rather than the base
            // renderer's boxed system checkbox bitmap, which arrives as a
            // light-themed image no color table can restyle.
            var b = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.AccentGreen, 1.8f);
            int x = b.Left + b.Width / 2 - 4;
            int y = b.Top + b.Height / 2;
            e.Graphics.DrawLines(pen, new[]
            {
                new Point(x, y),
                new Point(x + 3, y + 3),
                new Point(x + 9, y - 4),
            });
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            // One flat hairline instead of the default light/dark 3D pair.
            using var pen = new Pen(Theme.Border);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextSecondary;
            base.OnRenderArrow(e);
        }
    }

    /// <summary>The fills the base renderer resolves through a color table.
    /// Everything is mapped onto the app surface so no stray light-themed
    /// gradient survives anywhere in the menu.</summary>
    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.MenuHover;
        public override Color MenuItemSelected => Theme.MenuHover;
        public override Color MenuItemSelectedGradientBegin => Theme.MenuHover;
        public override Color MenuItemSelectedGradientEnd => Theme.MenuHover;
        public override Color MenuItemPressedGradientBegin => Theme.Surface;
        public override Color MenuItemPressedGradientMiddle => Theme.Surface;
        public override Color MenuItemPressedGradientEnd => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color CheckBackground => Theme.Surface;
        public override Color CheckSelectedBackground => Theme.MenuHover;
        public override Color CheckPressedBackground => Theme.MenuHover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}
