using static SocialBreakTray.Tr;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SocialBreakTray.Ui;

/// <summary>
/// An app's daily limit as one segmented control - 15m, 30m, 1h, 2h, Block -
/// the same every day, which is all a free account's rule is. Used where an
/// app is added (AddAppsForm) and where its limit is changed (LimitForm).
///
/// Drawn whole rather than built from five buttons: one dark track, the choice
/// lit as a gradient pill (red for Block), the rest quiet text that lifts on
/// hover - no box around each option.
/// </summary>
internal class LimitPicker : Control
{
    private static readonly int[] Minutes = { 15, 30, 60, 120 };
    private static readonly Color PillFrom = Color.FromArgb(0x8b, 0x5c, 0xf6);
    private static readonly Color PillTo = Color.FromArgb(0x5b, 0x8d, 0xf6);
    private static readonly Color BlockFrom = Color.FromArgb(0xf3, 0x8b, 0xa8);
    private static readonly Color BlockTo = Color.FromArgb(0xe0, 0x5a, 0x7a);
    private const int Inset = 4;

    private readonly string[] _labels;
    private int _selected = -1;
    private int _hovered = -1;

    public event EventHandler? Changed;

    public LimitPicker(int width, int height = 34)
    {
        Size = new Size(width, height);
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font(Theme.UiFont.FontFamily, 9.5f, FontStyle.Bold);
        _labels = Minutes.Select(Short).Append(T("Block", "Μπλοκ")).ToArray();
    }

    public bool HasChoice => _selected >= 0;
    public bool Block => _selected == Minutes.Length;
    public int? LimitMinutes => _selected >= 0 && _selected < Minutes.Length ? Minutes[_selected] : null;

    /// <summary>Shows a current limit, when it's one of the choices.</summary>
    public void SetCurrent(int? minutes, bool block)
    {
        _selected = block ? Minutes.Length : minutes is int m ? Array.IndexOf(Minutes, m) : -1;
        Invalidate();
    }

    private Rectangle Segment(int index)
    {
        int inner = Width - Inset * 2;
        int left = Inset + inner * index / _labels.Length;
        int right = Inset + inner * (index + 1) / _labels.Length;
        return new Rectangle(left, Inset, right - left, Height - Inset * 2);
    }

    private int IndexAt(Point p)
    {
        for (int i = 0; i < _labels.Length; i++)
            if (Segment(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int at = IndexAt(e.Location);
        if (at != _hovered) { _hovered = at; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int at = IndexAt(e.Location);
        if (at < 0) return;
        _selected = at;
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // The track.
        using (var track = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
        using (var trackBrush = new SolidBrush(Theme.Well))
        {
            g.FillPath(trackBrush, track);
        }

        for (int i = 0; i < _labels.Length; i++)
        {
            var seg = Segment(i);
            bool on = i == _selected;
            bool block = i == Minutes.Length;
            int radius = seg.Height / 2;

            if (on)
            {
                // A soft glow under the pill, then the pill.
                using (var glowPath = Theme.RoundedRect(Rectangle.Inflate(seg, 1, 1), radius + 1))
                using (var glow = new SolidBrush(Color.FromArgb(60, block ? BlockFrom : PillFrom)))
                {
                    g.FillPath(glow, glowPath);
                }
                using var pill = Theme.RoundedRect(seg, radius);
                using var fill = new LinearGradientBrush(seg, block ? BlockFrom : PillFrom, block ? BlockTo : PillTo, LinearGradientMode.Horizontal);
                g.FillPath(fill, pill);
            }
            else if (i == _hovered)
            {
                using var pill = Theme.RoundedRect(seg, radius);
                using var fill = new SolidBrush(Theme.Card);
                g.FillPath(fill, pill);
            }

            var colour = on ? Color.White : i == _hovered ? Theme.TextPrimary : Theme.TextSecondary;
            TextRenderer.DrawText(g, _labels[i], Font, seg, colour,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private static string Short(int minutes) =>
        minutes >= 60 ? $"{minutes / 60}{H}" : $"{minutes}{M}";
}
