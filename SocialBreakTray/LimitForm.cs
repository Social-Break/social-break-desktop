using static SocialBreakTray.Tr;
using System.Diagnostics;
using SocialBreakTray.Api;
using SocialBreakTray.Enforcement;
using SocialBreakTray.Ui;

namespace SocialBreakTray;

/// <summary>
/// One app's daily limit, opened by clicking it in Live Tracking - so a limit
/// is set where the app is, not on a trip to the website. A Premium schedule
/// (other days, times of day, a weekly limit) would be flattened by these
/// choices, so for one of those it says so and points to the website instead.
/// </summary>
internal class LimitForm : DarkForm
{
    private const int Pad = 28;
    private const int Inner = 360;

    private readonly SocialBreakApiClient _api;
    private readonly MediaItemDto _app;
    private readonly LimitPicker _picker = new(Inner);
    private readonly RoundedButton _save = new();
    private readonly Label _status = new();

    public LimitForm(SocialBreakApiClient api, MediaItemDto app, string displayName, PlanDto? plan)
        : base("Social Break", showMinimize: false)
    {
        _api = api;
        _app = app;
        StartPosition = FormStartPosition.CenterParent;

        int y = 24;
        var name = new Label
        {
            Text = displayName,
            ForeColor = Theme.TextPrimary,
            Font = new Font(Theme.UiFont.FontFamily, 14f, FontStyle.Bold),
            AutoSize = false,
            Location = new Point(Pad, y),
            Size = new Size(Inner, 30),
        };
        y += 30 + 4;
        Content.Controls.Add(name);

        var current = LimitEvaluator.SimpleRule(plan, app.Url);
        var question = new Label
        {
            Text = current.Schedule
                ? T("This app has its own schedule. Change it on the website.", "Αυτή η εφαρμογή έχει δικό της πρόγραμμα. Άλλαξέ το στον ιστότοπο.")
                : T("How much time a day?", "Πόσο χρόνο τη μέρα;"),
            ForeColor = Theme.TextSecondary,
            AutoSize = false,
            Location = new Point(Pad, y),
            Size = new Size(Inner, current.Schedule ? 38 : 20),
        };
        y += question.Height + 18;
        Content.Controls.Add(question);

        if (!current.Schedule)
        {
            _picker.Location = new Point(Pad, y);
            _picker.SetCurrent(current.Minutes, current.Block);
            _picker.Changed += (_, _) => _save.Enabled = true;
            y += _picker.Height + 18;
            Content.Controls.Add(_picker);
        }

        _save.Text = current.Schedule ? T("Open the website", "Άνοιξε τον ιστότοπο") : T("Save", "Αποθήκευση");
        _save.Location = new Point(Pad, y);
        _save.Size = new Size(Inner, 40);
        _save.BackColor = Theme.ButtonGreen;
        _save.ForeColor = Color.White;
        _save.HoverColor = Theme.ButtonGreenHover;
        _save.Font = new Font(Theme.UiFont.FontFamily, 10f, FontStyle.Bold);
        _save.Cursor = Cursors.Hand;
        _save.Enabled = current.Schedule;
        _save.Click += async (_, _) =>
        {
            if (current.Schedule) { OpenPlan(); Close(); return; }
            await SaveAsync();
        };
        y += 40 + 10;
        Content.Controls.Add(_save);

        _status.ForeColor = Theme.AccentRed;
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.TopCenter;
        _status.Location = new Point(Pad, y);
        _status.Size = new Size(Inner, 34);
        Content.Controls.Add(_status);
        y += 34;

        if (!current.Schedule)
        {
            // Premium's finer rules are a bigger editor than a window this size.
            var more = new LinkLabel
            {
                Text = T("More options on the website →", "Περισσότερες επιλογές στον ιστότοπο →"),
                LinkColor = Theme.TextSecondary,
                ActiveLinkColor = Theme.TextPrimary,
                VisitedLinkColor = Theme.TextSecondary,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = new Font(Theme.UiFont.FontFamily, 8.5f),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(Pad, y),
                Size = new Size(Inner, 20),
            };
            more.LinkClicked += (_, _) => OpenPlan();
            Content.Controls.Add(more);
            y += 20;
        }

        SetContentSize(Inner + Pad * 2, y + 22);
        AcceptButton = _save;
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

    private static void OpenPlan()
    {
        try { Process.Start(new ProcessStartInfo(SocialBreakApiClient.SiteUrl + "/plan/") { UseShellExecute = true }); }
        catch { /* nothing to open it with */ }
    }
}
