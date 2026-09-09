using System.Drawing.Drawing2D;

namespace SocialBreakTray.Ui;

/// <summary>
/// A text box in a rounded, themed well.
///
/// WinForms only offers a TextBox a square single-pixel border or none at
/// all, which is why every field in this app used to end in hard corners
/// while the windows around them were rounded. So the border is not the
/// box's: the box is borderless and sits inside a panel that paints the
/// rounded well behind it, which also gives somewhere to put the breathing
/// room a 34-pixel-tall field never had.
///
/// The outline picks up the accent colour while the box has focus - the only
/// cue left once the placeholder disappears, since WinForms hides that the
/// moment a box is focused.
/// </summary>
internal class RoundedField : Panel
{
    private const int Radius = 8;
    private const int SidePadding = 12;

    public TextBox Box { get; } = new();

    public RoundedField(string placeholder, int width, int height)
    {
        Size = new Size(width, height);
        // Matches the surface it sits on, so the corners the rounded path
        // leaves out read as background rather than as artefacts.
        BackColor = Theme.Bg;
        DoubleBuffered = true;

        Box.BorderStyle = BorderStyle.None;
        Box.BackColor = Theme.Well;
        Box.ForeColor = Theme.TextPrimary;
        Box.Font = new Font(Theme.UiFont.FontFamily, 10.5f);
        Box.PlaceholderText = placeholder;
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();

        Controls.Add(Box);
        LayoutBox();
    }

    /// <summary>Height comes from the font when a TextBox has no border, so
    /// it can only be centred after the font is settled.</summary>
    public void LayoutBox()
    {
        Box.Width = Width - SidePadding * 2;
        Box.Location = new Point(SidePadding, Math.Max(0, (Height - Box.Height) / 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Radius);
        using (var fill = new SolidBrush(Theme.Well))
        {
            e.Graphics.FillPath(fill, path);
        }
        using var pen = new Pen(Box.Focused ? Theme.AccentGreen : Theme.Border);
        e.Graphics.DrawPath(pen, path);
    }
}
