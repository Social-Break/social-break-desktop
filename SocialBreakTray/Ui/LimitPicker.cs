using static SocialBreakTray.Tr;

namespace SocialBreakTray.Ui;

/// <summary>
/// An app's daily limit in one row of choices - 15m, 30m, 1h, 2h, or Block -
/// the same every day, which is all a free account's rule is. Used where an
/// app is added (AddAppsForm) and where its limit is changed (LimitForm).
/// </summary>
internal class LimitPicker : Panel
{
    private static readonly int[] Minutes = { 15, 30, 60, 120 };
    private readonly List<RoundedButton> _chips = new();
    private int _selected = -1;

    public event EventHandler? Changed;

    public LimitPicker(int width, int height = 34)
    {
        Size = new Size(width, height);
        BackColor = Color.Transparent;

        const int gap = 8;
        int count = Minutes.Length + 1;
        int chipWidth = (width - gap * (count - 1)) / count;
        for (int i = 0; i < count; i++)
        {
            var chip = new RoundedButton
            {
                Text = i < Minutes.Length ? Short(Minutes[i]) : T("Block", "Μπλοκ"),
                Location = new Point(i * (chipWidth + gap), 0),
                Size = new Size(chipWidth, height),
                Font = new Font(Theme.UiFont.FontFamily, 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
            };
            int index = i;
            chip.Click += (_, _) => Choose(index);
            _chips.Add(chip);
            Controls.Add(chip);
        }
        Restyle();
    }

    public bool HasChoice => _selected >= 0;
    public bool Block => _selected == Minutes.Length;
    public int? LimitMinutes => _selected >= 0 && _selected < Minutes.Length ? Minutes[_selected] : null;

    /// <summary>Shows a current limit, when it's one of the choices.</summary>
    public void SetCurrent(int? minutes, bool block)
    {
        _selected = block ? Minutes.Length : minutes is int m ? Array.IndexOf(Minutes, m) : -1;
        Restyle();
    }

    private void Choose(int index)
    {
        _selected = index;
        Restyle();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Restyle()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            bool on = i == _selected;
            bool block = i == Minutes.Length;
            _chips[i].BackColor = on ? (block ? Theme.AccentRed : Theme.AccentGreen) : Theme.Card;
            _chips[i].ForeColor = on ? Theme.Well : Theme.TextPrimary;
            _chips[i].OutlineColor = on ? Color.Empty : Theme.Border;
            _chips[i].HoverColor = on ? _chips[i].BackColor : Theme.MenuHover;
            _chips[i].Invalidate();
        }
    }

    private static string Short(int minutes) =>
        minutes >= 60 ? $"{minutes / 60}{H}" : $"{minutes}{M}";

}
