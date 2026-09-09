using System.Drawing.Drawing2D;

namespace SocialBreakTray.Ui;

/// <summary>
/// A flat button with rounded corners, painted rather than clipped - setting
/// a Region would round the shape but leave the corners jagged, which is
/// worse than leaving them square. Exists so the buttons match the fields
/// they sit under; a window mixing both radii is what "unprofessional" looks
/// like up close.
/// </summary>
internal class RoundedButton : Button
{
    private bool _hovered;

    public int Radius { get; set; } = 8;
    public Color HoverColor { get; set; } = Color.Empty;
    public Color OutlineColor { get; set; } = Color.Empty;

    public RoundedButton()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Radius);

        var fill = BackColor;
        if (!Enabled) fill = Theme.Card;
        else if (_hovered && HoverColor != Color.Empty) fill = HoverColor;

        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        if (OutlineColor != Color.Empty)
        {
            using var pen = new Pen(OutlineColor);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            g, Text, Font, rect, Enabled ? ForeColor : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
