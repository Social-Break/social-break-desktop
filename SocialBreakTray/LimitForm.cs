using static SocialBreakTray.Tr;
using System.Diagnostics;
using SocialBreakTray.Api;
using SocialBreakTray.Enforcement;
using SocialBreakTray.Tracking;
using SocialBreakTray.Ui;

namespace SocialBreakTray;

/// <summary>
/// One app's daily limit, opened by clicking it in Live Tracking - so a limit
/// is set where the app is, not on a trip to the website. A Premium schedule
/// (other days, times of day, a weekly limit) would be flattened by these
/// choices, so for one of those it says so and opens the website's editor for
/// this app instead. "More options" opens that same editor, with the app
/// already picked.
/// </summary>
internal class LimitForm : DarkForm
{
    private const int Pad = 28;
    private const int Inner = 380;

    private readonly SocialBreakApiClient _api;
    private readonly MediaItemDto _app;
    private readonly LimitPicker _picker = new(Inner, 40);
    private readonly RoundedButton _save = new();
    private readonly Label _status = new();

    public LimitForm(SocialBreakApiClient api, MediaItemDto app, string displayName, PlanDto? plan)
        : base("Social Break", showMinimize: false)
    {
        _api = api;
        _app = app;
        StartPosition = FormStartPosition.CenterParent;
        var current = LimitEvaluator.SimpleRule(plan, app.Url);

        // The app as Live Tracking shows it: its icon, its name, and what its
        // limit is now.
        int y = 24;
        var (_, icon) = AppLook.For(app.Url, 36);
        var iconBox = new Panel { Location = new Point(Pad, y + 2), Size = new Size(36, 36), BackColor = Color.Transparent };
        iconBox.Paint += (_, e) =>
        {
            if (icon != null) e.Graphics.DrawImage(icon, 0, 0, 36, 36);
        };
        var name = new Label
        {
            Text = displayName,
            ForeColor = Theme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 14f),
            AutoSize = false,
            Location = new Point(Pad + 48, y - 2),
            Size = new Size(Inner - 48, 26),
        };
        var now = new Label
        {
            Text = current.Schedule ? T("Has its own schedule", "Έχει δικό της πρόγραμμα")
                : current.Block ? T("Blocked every day", "Μπλοκαρισμένη κάθε μέρα")
                : current.Minutes is int m ? T($"{Short(m)} a day", $"{Short(m)} τη μέρα")
                : T("No limit yet", "Χωρίς όριο ακόμα"),
            ForeColor = current.Block ? Theme.AccentRed : Theme.TextSecondary,
            AutoSize = false,
            Location = new Point(Pad + 48, y + 22),
            Size = new Size(Inner - 48, 18),
        };
        Content.Controls.AddRange(new Control[] { iconBox, name, now });
        y += 40 + 26;

        if (current.Schedule)
        {
            var note = new Label
            {
                Text = T("Its different days or times can only be changed on the website.",
                    "Οι διαφορετικές μέρες ή ώρες της αλλάζουν μόνο στον ιστότοπο."),
                ForeColor = Theme.TextSecondary,
                AutoSize = false,
                Location = new Point(Pad, y),
                Size = new Size(Inner, 38),
            };
            Content.Controls.Add(note);
            y += 38 + 16;
        }
        else
        {
            var label = new Label
            {
                Text = T("DAILY LIMIT", "ΗΜΕΡΗΣΙΟ ΟΡΙΟ"),
                ForeColor = Theme.TextMuted,
                Font = new Font(Theme.UiFont.FontFamily, 8f, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(Pad, y),
                Size = new Size(Inner, 16),
            };
            y += 16 + 8;
            _picker.Location = new Point(Pad, y);
            _picker.SetCurrent(current.Minutes, current.Block);
            _picker.Changed += (_, _) => _save.Enabled = true;
            Content.Controls.AddRange(new Control[] { label, _picker });
            y += _picker.Height + 18;

            _save.Text = T("Save", "Αποθήκευση");
            _save.Location = new Point(Pad, y);
            _save.Size = new Size(Inner, 42);
            _save.BackColor = Theme.ButtonGreen;
            _save.ForeColor = Color.White;
            _save.HoverColor = Theme.ButtonGreenHover;
            _save.Font = new Font(Theme.UiFont.FontFamily, 10f, FontStyle.Bold);
            _save.Cursor = Cursors.Hand;
            _save.Enabled = false;
            _save.Click += async (_, _) => await SaveAsync();
            Content.Controls.Add(_save);
            AcceptButton = _save;
            y += 42 + 6;

            _status.ForeColor = Theme.AccentRed;
            _status.AutoSize = false;
            _status.TextAlign = ContentAlignment.TopCenter;
            _status.Location = new Point(Pad, y);
            _status.Size = new Size(Inner, 32);
            Content.Controls.Add(_status);
            y += 32;

            Content.Controls.Add(new Panel { Location = new Point(Pad, y), Size = new Size(Inner, 1), BackColor = Theme.Border });
            y += 1 + 16;
        }

        // A button, not a footnote: different days, times of day and weekly
        // limits are real options, and this opens them for this very app.
        var website = new RoundedButton
        {
            Text = current.Schedule
                ? T("Open it on the website  ↗", "Άνοιξέ το στον ιστότοπο  ↗")
                : T("More options on the website  ↗", "Περισσότερες επιλογές στον ιστότοπο  ↗"),
            Location = new Point(Pad, y),
            Size = new Size(Inner, 40),
            BackColor = current.Schedule ? Theme.ButtonGreen : Theme.Card,
            ForeColor = current.Schedule ? Color.White : Theme.TextPrimary,
            OutlineColor = current.Schedule ? Color.Empty : Theme.Border,
            HoverColor = current.Schedule ? Theme.ButtonGreenHover : Theme.MenuHover,
            Font = new Font(Theme.UiFont.FontFamily, 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        website.Click += (_, _) => { OpenRules(); if (current.Schedule) Close(); };
        Content.Controls.Add(website);
        if (current.Schedule) AcceptButton = website;
        y += 40;

        SetContentSize(Inner + Pad * 2, y + 26);
    }

    private async Task SaveAsync()
    {
        if (!_picker.HasChoice) return;
        _save.Enabled = false;
        _status.Text = "";
        string? problem;
        try
        {
            problem = await _api.SetDailyLimitAsync(_app.Id, _picker.LimitMinutes, _picker.Block);
        }
        catch
        {
            problem = T("Couldn't reach Social Break. Check your internet and try again.", "Δεν υπάρχει σύνδεση με το Social Break. Έλεγξε το ίντερνετ και δοκίμασε ξανά.");
        }
        if (problem == null)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        _status.Text = problem;
        _save.Enabled = true;
    }

    /// <summary>The website's rule editor, with this app already ticked.</summary>
    private void OpenRules()
    {
        var url = $"{SocialBreakApiClient.SiteUrl}/plan/?app={_app.Id}";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* nothing to open it with */ }
    }

    private static string Short(int minutes) =>
        minutes >= 60 ? (minutes % 60 == 0 ? $"{minutes / 60}{H}" : $"{minutes / 60}{H} {minutes % 60}{M}") : $"{minutes}{M}";
}
