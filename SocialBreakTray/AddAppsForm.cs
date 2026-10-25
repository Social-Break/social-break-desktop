using static SocialBreakTray.Tr;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SocialBreakTray.Api;
using SocialBreakTray.Tracking;
using SocialBreakTray.Ui;

namespace SocialBreakTray;

/// <summary>
/// "Which apps do you want to limit?" - the programs on this computer, by
/// the names and icons people already know, the most used this week first.
/// Tick a few, press Add, and they're on the list. Opens by itself right
/// after this computer is connected (while nothing from it is on the list
/// yet), and any time from the tray's "Add apps…".
///
/// The one thing this app changes on the list: adding. Removing, limits and
/// rules stay on the website.
/// </summary>
internal class AddAppsForm : DarkForm
{
    private const int ContentWidth = 440;
    // From the top down: the list, a line for its status, the buttons, and a
    // margin under them. The height was a fixed 560, which put the status
    // line and the buttons on top of each other and the buttons on the edge.
    private const int ListTop = 128;
    private const int StatusTop = ListTop + RowHeight * 8 + 8;
    private const int ButtonsTop = StatusTop + 40;
    private const int ContentHeight = ButtonsTop + 40 + 24;
    private const int Pad = 20;
    private const int RowHeight = 42;
    private const int IconSize = 24;
    private const int BoxSize = 16;

    private static readonly Font NameFont = new(Theme.UiFont.FontFamily, 10f);
    private static readonly Font LetterFont = new(Theme.UiFont, FontStyle.Bold);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(nint hWnd, string? appName, string? idList);

    private sealed class Row
    {
        public required string Exe { get; init; }
        public required string Name { get; init; }
        public string? Path { get; init; }
        public int Minutes { get; init; }
        public Bitmap? Icon { get; set; }
        public bool OnList { get; set; }
        public bool Ticked { get; set; }
    }

    private readonly SocialBreakApiClient _api;
    private readonly IReadOnlyList<TopApp> _mostUsed;
    private readonly IEnumerable<string> _knownOnList;
    private readonly List<Row> _rows = new();

    private readonly RoundedField _search = new(T("Search", "Αναζήτηση"), ContentWidth - Pad * 2, 36);
    private readonly ListBox _list = new();
    private readonly Label _status = new();
    private readonly RoundedButton _addButton = new();
    private readonly RoundedButton _laterButton = new();
    private int _hovered = -1;
    private bool _busy;

    /// <summary>The names put on the list while this window was open.</summary>
    public List<string> Added { get; } = new();

    /// <param name="mostUsed">This week's most used programs, most first.</param>
    /// <param name="knownOnList">Programs already on the list as last synced -
    /// used when the list itself can't be fetched fresh.</param>
    public AddAppsForm(SocialBreakApiClient api, IReadOnlyList<TopApp> mostUsed, IEnumerable<string> knownOnList)
        : base(T("Social Break - Add apps", "Social Break - Προσθήκη εφαρμογών"), showMinimize: false)
    {
        _api = api;
        _mostUsed = mostUsed;
        _knownOnList = knownOnList.ToList();

        SetContentSize(ContentWidth, ContentHeight);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };

        var title = new Label
        {
            Text = T("Which apps do you want to limit?", "Ποιες εφαρμογές θέλεις να περιορίσεις;"),
            ForeColor = Theme.TextPrimary,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(Pad - 2, 18),
        };
        var subtitle = new Label
        {
            Text = T("Tick the ones that take too much of your time.", "Διάλεξε αυτές που σου τρώνε πολύ χρόνο."),
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Location = new Point(Pad, 50),
        };

        _search.Location = new Point(Pad, 80);
        _search.Box.TextChanged += (_, _) => ShowRows();
        _search.Enabled = false;

        _list.Location = new Point(Pad, ListTop);
        _list.Size = new Size(ContentWidth - Pad * 2, RowHeight * 8);
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Bg;
        _list.ForeColor = Theme.TextPrimary;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = RowHeight;
        _list.IntegralHeight = false;
        _list.DrawItem += DrawRow;
        _list.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Toggle(_list.IndexFromPoint(e.Location));
        };
        _list.MouseMove += (_, e) => SetHovered(_list.IndexFromPoint(e.Location));
        _list.MouseLeave += (_, _) => SetHovered(-1);
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space) return;
            Toggle(_list.SelectedIndex);
            e.Handled = true;
        };
        // Windows' own dark scroll bar, rather than a bright light one down
        // the side of a dark window.
        _list.HandleCreated += (_, _) => SetWindowTheme(_list.Handle, "DarkMode_Explorer", null);

        _status.ForeColor = Theme.TextMuted;
        _status.AutoSize = false;
        _status.Location = new Point(Pad, StatusTop);
        _status.Size = new Size(ContentWidth - Pad * 2, 34);
        _status.Text = T("Looking for your apps…", "Ψάχνω τις εφαρμογές σου…");

        int buttonsTop = ButtonsTop;
        _laterButton.Text = T("Close", "Κλείσιμο");
        _laterButton.Location = new Point(Pad, buttonsTop);
        _laterButton.Size = new Size(130, 40);
        _laterButton.BackColor = Theme.Card;
        _laterButton.ForeColor = Theme.TextPrimary;
        _laterButton.OutlineColor = Theme.Border;
        _laterButton.HoverColor = Theme.MenuHover;
        _laterButton.Font = new Font(Theme.UiFont.FontFamily, 10f);
        _laterButton.Click += (_, _) => Close();

        _addButton.Text = T("Add to my list", "Προσθήκη στη λίστα μου");
        _addButton.Location = new Point(Pad + 130 + 12, buttonsTop);
        _addButton.Size = new Size(ContentWidth - Pad * 2 - 130 - 12, 40);
        _addButton.BackColor = Theme.ButtonGreen;
        _addButton.ForeColor = Color.White;
        _addButton.HoverColor = Theme.ButtonGreenHover;
        _addButton.Font = new Font(Theme.UiFont.FontFamily, 10f, FontStyle.Bold);
        _addButton.Enabled = false;
        _addButton.Click += async (_, _) => await AddTickedAsync();

        Content.Controls.AddRange(new Control[] { title, subtitle, _search, _list, _status, _laterButton, _addButton });
        Shown += async (_, _) => await LoadAsync();
        FormClosed += (_, _) =>
        {
            foreach (var row in _rows) row.Icon?.Dispose();
        };
    }

    /// <summary>Reads the Start menu and the icons off the window's own
    /// thread - a few hundred shortcuts take a moment - while asking the
    /// website what's already on the list.</summary>
    private async Task LoadAsync()
    {
        var installedTask = Task.Run(InstalledApps.Find);
        HashSet<string> onList;
        try
        {
            var items = await _api.GetMediaItemsAsync();
            onList = items.Where(m => m.SourceType == "desktop_app").Select(m => m.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            onList = _knownOnList.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var installed = await installedTask;

        var byExe = installed.ToDictionary(a => a.Exe, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var used in _mostUsed)
        {
            if (!seen.Add(used.Exe)) continue;
            var app = byExe.GetValueOrDefault(used.Exe);
            _rows.Add(new Row { Exe = used.Exe, Name = app?.Name ?? used.Name, Path = app?.Path, Minutes = used.Minutes, OnList = onList.Contains(used.Exe) });
        }
        foreach (var app in installed)
        {
            if (!seen.Add(app.Exe)) continue;
            _rows.Add(new Row { Exe = app.Exe, Name = app.Name, Path = app.Path, OnList = onList.Contains(app.Exe) });
        }

        await Task.Run(() =>
        {
            foreach (var row in _rows) row.Icon = IconOf(row.Path);
        });
        if (IsDisposed) return;

        _search.Enabled = true;
        _status.Text = _rows.Count == 0
            ? T("No apps found on this computer. You can add one by name on the website.", "Δεν βρέθηκαν εφαρμογές σε αυτόν τον υπολογιστή. Μπορείς να προσθέσεις μία με το όνομά της στον ιστότοπο.")
            : "";
        ShowRows();
        _search.Box.Focus();
    }

    private static Bitmap? IconOf(string? path) => AppLook.IconOf(path, IconSize);

    /// <summary>The rows the search leaves, in their order.</summary>
    private void ShowRows()
    {
        var query = _search.Box.Text.Trim();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in _rows)
        {
            if (query.Length == 0 || row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || row.Exe.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _list.Items.Add(row);
            }
        }
        _list.EndUpdate();
        _hovered = -1;
        if (_rows.Count > 0 && _list.Items.Count == 0)
        {
            _status.ForeColor = Theme.TextMuted;
            _status.Text = T("Nothing called that here. You can add it by name on the website.", "Δεν υπάρχει κάτι με αυτό το όνομα. Μπορείς να το προσθέσεις με το όνομά του στον ιστότοπο.");
        }
        else if (!_busy)
        {
            UpdateCount();
        }
    }

    private void Toggle(int index)
    {
        if (_busy || index < 0 || index >= _list.Items.Count || _list.Items[index] is not Row row || row.OnList) return;
        row.Ticked = !row.Ticked;
        _list.Invalidate(_list.GetItemRectangle(index));
        UpdateCount();
    }

    private void SetHovered(int index)
    {
        if (index == _hovered) return;
        if (_hovered >= 0 && _hovered < _list.Items.Count) _list.Invalidate(_list.GetItemRectangle(_hovered));
        _hovered = index;
        if (index >= 0 && index < _list.Items.Count) _list.Invalidate(_list.GetItemRectangle(index));
        _list.Cursor = index >= 0 && _list.Items[index] is Row { OnList: false } ? Cursors.Hand : Cursors.Default;
    }

    private void UpdateCount()
    {
        int ticked = _rows.Count(r => r.Ticked);
        _addButton.Enabled = ticked > 0;
        _status.ForeColor = Theme.TextMuted;
        _status.Text = ticked switch
        {
            0 => "",
            1 => T("1 app picked", "1 εφαρμογή"),
            _ => T($"{ticked} apps picked", $"{ticked} εφαρμογές"),
        };
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count || _list.Items[e.Index] is not Row row) return;
        var g = e.Graphics;
        var bounds = e.Bounds;
        bool hovered = e.Index == _hovered && !row.OnList;

        using (var back = new SolidBrush(Theme.Bg))
        {
            g.FillRectangle(back, bounds);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (hovered || row.Ticked)
        {
            using var path = Theme.RoundedRect(new Rectangle(bounds.X, bounds.Y + 2, bounds.Width - 1, bounds.Height - 5), 8);
            using var fill = new SolidBrush(row.Ticked ? Theme.Surface : Theme.Card);
            g.FillPath(fill, path);
        }

        // The tick box, as DarkCheckBox draws it - filled once ticked, and
        // already ticked but faded for what's on the list.
        var box = new Rectangle(bounds.X + 12, bounds.Y + (bounds.Height - BoxSize) / 2, BoxSize, BoxSize);
        bool filled = row.Ticked || row.OnList;
        var boxColor = row.OnList ? Theme.TextMuted : Theme.AccentGreen;
        using (var path = Theme.RoundedRect(box, 3))
        {
            using var fill = new SolidBrush(filled ? boxColor : Theme.Surface);
            g.FillPath(fill, path);
            using var edge = new Pen(filled ? boxColor : hovered ? Theme.TextMuted : Theme.Border);
            g.DrawPath(edge, path);
        }
        if (filled)
        {
            using var tick = new Pen(Theme.Bg, 2f);
            int x = box.Left + 4, y = box.Top + box.Height / 2;
            g.DrawLines(tick, new[] { new Point(x, y), new Point(x + 3, y + 3), new Point(x + 9, y - 4) });
        }

        var iconRect = new Rectangle(box.Right + 14, bounds.Y + (bounds.Height - IconSize) / 2, IconSize, IconSize);
        if (row.Icon != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(row.Icon, iconRect);
        }
        else
        {
            // No icon to show: its first letter, in a soft circle.
            using var circle = new SolidBrush(Theme.Avatar);
            g.FillEllipse(circle, iconRect);
            TextRenderer.DrawText(g, row.Name[..1].ToUpperInvariant(), LetterFont, iconRect, Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        string note = row.OnList
            ? T("On your list", "Στη λίστα σου")
            : row.Minutes > 0 ? T($"{Duration(row.Minutes)} this week", $"{Duration(row.Minutes)} αυτή την εβδομάδα") : "";
        int noteWidth = note.Length == 0 ? 0 : TextRenderer.MeasureText(g, note, Theme.UiFont).Width + 4;
        var noteRect = new Rectangle(bounds.Right - noteWidth - 12, bounds.Y, noteWidth, bounds.Height);

        int textLeft = iconRect.Right + 12;
        var nameRect = new Rectangle(textLeft, bounds.Y, Math.Max(0, noteRect.Left - textLeft - 8), bounds.Height);
        TextRenderer.DrawText(g, row.Name, NameFont, nameRect,
            row.OnList ? Theme.TextMuted : Theme.TextPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (note.Length > 0)
        {
            TextRenderer.DrawText(g, note, Theme.UiFont, noteRect, Theme.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        }
    }

    private static string Duration(int minutes) =>
        minutes >= 60 ? (minutes % 60 == 0 ? $"{minutes / 60}{H}" : $"{minutes / 60}{H} {minutes % 60}{M}") : $"{minutes}{M}";

    private async Task AddTickedAsync()
    {
        var picked = _rows.Where(r => r.Ticked && !r.OnList).ToList();
        if (picked.Count == 0) return;

        _busy = true;
        _addButton.Enabled = false;
        _laterButton.Enabled = false;
        _status.ForeColor = Theme.TextMuted;
        _status.Text = T("Adding…", "Προσθήκη…");

        string? problem = null;
        foreach (var row in picked)
        {
            try
            {
                problem = await _api.AddAppAsync(row.Exe, row.Name);
            }
            catch
            {
                problem = T("Couldn't reach Social Break. Check your internet and try again.", "Δεν υπάρχει σύνδεση με το Social Break. Έλεγξε το ίντερνετ και δοκίμασε ξανά.");
            }
            if (problem != null) break;
            row.Ticked = false;
            row.OnList = true;
            Added.Add(row.Name);
            _list.Invalidate();
        }

        _busy = false;
        _laterButton.Enabled = true;
        if (problem == null)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        UpdateCount();
        _status.ForeColor = Theme.AccentRed;
        _status.Text = problem;
    }
}
