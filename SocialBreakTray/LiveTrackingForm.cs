using System.Drawing.Drawing2D;
using SocialBreakTray.Api;
using SocialBreakTray.Auth;
using SocialBreakTray.Enforcement;
using SocialBreakTray.Tracking;
using SocialBreakTray.Ui;

namespace SocialBreakTray;

/// <summary>
/// A live view of today's/this week's accrued time per tracked desktop app,
/// with the currently-focused one highlighted and a progress bar against
/// its real daily limit (when one applies). Deliberately read-only - no
/// limits, rules, or Media List editing here, that's still exclusively the
/// website's job (see LimitEvaluator/legal.html). This exists purely so the
/// user can see tracking actually happening in real time without leaving
/// the desktop, not to become a second settings surface.
///
/// Shown automatically on every launch by default (see
/// TrayApplicationContext.InitializeAsync) - its own checkbox lets the user
/// opt out of that, while it stays reachable on demand via the tray menu or
/// double-clicking the icon regardless of that preference.
///
/// Non-modal and reused across openings (TrayApplicationContext keeps a
/// single reference and just re-activates it on repeat clicks, same pattern
/// as _activeBlockForm) - refreshes itself on a timer while open, reading
/// live off the same UsageAccumulator/tracked-apps-list/current-focus/plan
/// state the heartbeat itself updates, so it always reflects the real state
/// without needing any push/event wiring back from TrayApplicationContext.
/// Cards are kept and updated in place rather than rebuilt every tick, to
/// avoid flicker.
///
/// Chrome comes from DarkForm: the caption bar is drawn by the app in the
/// same gradient as the banner below it, so the two read as one header
/// instead of a themed window wearing a white Windows title bar.
/// </summary>
internal class LiveTrackingForm : DarkForm
{
    private const int ContentWidth = 460;
    private const int ContentHeight = 480;

    private readonly UsageAccumulator _accumulator;
    private readonly Func<List<MediaItemDto>> _getTrackedApps;
    private readonly Func<string?> _getCurrentlyTrackedUrl;
    private readonly Func<PlanDto?> _getPlan;
    private readonly int _resetHour;

    private readonly FlowLayoutPanel _cardList;
    private readonly Dictionary<string, TrackedAppCard> _cards = new();
    private readonly Label _emptyLabel;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public LiveTrackingForm(UsageAccumulator accumulator, Func<List<MediaItemDto>> getTrackedApps,
        Func<string?> getCurrentlyTrackedUrl, Func<PlanDto?> getPlan, int resetHour)
        : base("Social Break - Live Tracking", showMinimize: true)
    {
        _accumulator = accumulator;
        _getTrackedApps = getTrackedApps;
        _getCurrentlyTrackedUrl = getCurrentlyTrackedUrl;
        _getPlan = getPlan;
        _resetHour = resetHour;

        SetContentSize(ContentWidth, ContentHeight);

        var header = new Panel { Location = new Point(0, 0), Size = new Size(ContentWidth, 76) };
        header.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // Same brush and same width as the caption bar directly above,
            // so the horizontal gradient continues across the seam.
            using var gradient = Theme.CreateHeaderBrush(header.ClientRectangle);
            e.Graphics.FillRectangle(gradient, header.ClientRectangle);

            // 40px has no matching frame in app.ico, so this resamples the
            // 256px one rather than stretching the 32px one (see Theme).
            Theme.DrawAppIcon(e.Graphics, new Rectangle(20, 18, 40, 40));
        };

        var title = new Label
        {
            Text = "Live Tracking",
            ForeColor = Theme.TextPrimary,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(72, 16),
            BackColor = Color.Transparent,
        };
        var subtitle = new Label
        {
            Text = "Desktop apps counted against your Media List",
            ForeColor = Theme.TextSecondary,
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = true,
            Location = new Point(72, 44),
            BackColor = Color.Transparent,
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        _cardList = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Location = new Point(20, 92),
            Size = new Size(420, 300),
            BackColor = Theme.Bg,
        };

        _emptyLabel = new Label
        {
            Text = "No desktop apps on your Media List yet - add one on the website.",
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Visible = false,
        };

        // A hairline above the footer, separating live data from the
        // static explanatory text under it.
        var divider = new Panel
        {
            Location = new Point(20, 396),
            Size = new Size(420, 1),
            BackColor = Theme.Border,
        };

        var note = new Label
        {
            Text = "Limits, rules, and your Media List are managed on the website.",
            ForeColor = Theme.TextMuted,
            Font = new Font("Segoe UI", 8),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Location = new Point(20, 410),
        };

        // Only affects whether this window auto-opens on launch (see
        // TrayApplicationContext.InitializeAsync) - opening it from the
        // tray menu or double-clicking the icon always shows it regardless,
        // since that's an explicit request, not the automatic popup.
        // DarkCheckBox rather than a stock CheckBox, whose system-drawn
        // glyph stays a bright light-theme square whatever colors it's
        // given (see DarkCheckBox). Explicitly sized, since it owner-draws.
        var hideCheckbox = new DarkCheckBox
        {
            Text = "Don't show this automatically on startup",
            Location = new Point(20, 434),
            Size = new Size(320, 20),
            Checked = TokenStore.IsWelcomeHiddenOnStartup(),
        };
        hideCheckbox.CheckedChanged += (_, _) => TokenStore.SetHideWelcomeOnStartup(hideCheckbox.Checked);

        Content.Controls.AddRange(new Control[] { header, _cardList, _emptyLabel, divider, note, hideCheckbox });

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => RefreshCards();
        _refreshTimer.Start();
        FormClosed += (_, _) => _refreshTimer.Stop();

        RefreshCards();
    }

    private void RefreshCards()
    {
        var trackedApps = _getTrackedApps();
        var currentUrl = _getCurrentlyTrackedUrl();
        var plan = _getPlan();

        _emptyLabel.Visible = trackedApps.Count == 0;
        if (trackedApps.Count == 0 && !_cardList.Controls.Contains(_emptyLabel))
        {
            _cardList.Controls.Add(_emptyLabel);
        }

        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in trackedApps)
        {
            seenUrls.Add(app.Url);
            if (!_cards.TryGetValue(app.Url, out var card))
            {
                card = new TrackedAppCard(app.Name);
                _cards[app.Url] = card;
                _cardList.Controls.Add(card);
            }

            int daily = _accumulator.DailySeconds.GetValueOrDefault(app.Url);
            int weekly = _accumulator.WeeklySeconds.GetValueOrDefault(app.Url);
            int dailyLimit = LimitEvaluator.GetDailyLimitSeconds(app.Url, plan, _resetHour);
            bool isLive = string.Equals(app.Url, currentUrl, StringComparison.OrdinalIgnoreCase);
            card.UpdateData(daily, weekly, dailyLimit, isLive);
        }

        // Drop cards for apps no longer on the Media List (removed on the
        // website since the last sync).
        foreach (var staleUrl in _cards.Keys.Except(seenUrls).ToList())
        {
            _cardList.Controls.Remove(_cards[staleUrl]);
            _cards[staleUrl].Dispose();
            _cards.Remove(staleUrl);
        }
    }

    /// <summary>One tracked app's row - an avatar badge, name, a LIVE badge
    /// when active, and either a progress bar (when a real daily limit
    /// applies) or plain totals otherwise. Kept alive and updated in place
    /// across refresh ticks rather than rebuilt, both for smoothness and so
    /// the daily-limit-relative progress bar doesn't need to be recreated
    /// every second.</summary>
    private class TrackedAppCard : Panel
    {
        private readonly string _appName;
        private readonly Label _nameLabel;
        private readonly Label _liveLabel;
        private readonly Label _timeLabel;
        private readonly ProgressTrack _progressTrack;

        public TrackedAppCard(string appName)
        {
            _appName = appName;
            Size = new Size(420, 76);
            Margin = new Padding(0, 0, 0, 10);
            BackColor = Theme.Card;
            Region = new Region(Theme.RoundedRect(new Rectangle(0, 0, Width, Height), 12));

            var avatar = new Panel
            {
                Size = new Size(40, 40),
                Location = new Point(16, 18),
                BackColor = Color.Transparent,
            };
            avatar.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var bg = new SolidBrush(Theme.Avatar);
                e.Graphics.FillEllipse(bg, 0, 0, 39, 39);
                var letter = _appName.Length > 0 ? _appName[..1].ToUpperInvariant() : "?";
                using var font = new Font("Segoe UI", 13, FontStyle.Bold);
                var size = e.Graphics.MeasureString(letter, font);
                e.Graphics.DrawString(letter, font, Brushes.White, (40 - size.Width) / 2, (40 - size.Height) / 2);
            };

            _nameLabel = new Label
            {
                Text = appName,
                ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(68, 14),
                BackColor = Color.Transparent,
            };

            _liveLabel = new Label
            {
                Text = "●  LIVE",
                ForeColor = Theme.AccentGreen,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                AutoSize = true,
                BackColor = Color.Transparent,
                Visible = false,
            };
            _liveLabel.Location = new Point(Width - 20 - _liveLabel.PreferredWidth, 16);

            _timeLabel = new Label
            {
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(68, 36),
                BackColor = Color.Transparent,
            };

            _progressTrack = new ProgressTrack
            {
                Location = new Point(68, 56),
                Size = new Size(336, 6),
                Visible = false,
            };

            Controls.AddRange(new Control[] { avatar, _nameLabel, _liveLabel, _timeLabel, _progressTrack });
        }

        public void UpdateData(int dailySeconds, int weeklySeconds, int dailyLimitSeconds, bool isLive)
        {
            BackColor = isLive ? Theme.CardLive : Theme.Card;
            _liveLabel.Visible = isLive;

            if (dailyLimitSeconds > 0)
            {
                _timeLabel.Text = $"{TrayApplicationContext.FormatTime(dailySeconds)} of {TrayApplicationContext.FormatTime(dailyLimitSeconds)} today  ·  {TrayApplicationContext.FormatTime(weeklySeconds)} this week";
                _progressTrack.Percent = Math.Min(1.0, (double)dailySeconds / dailyLimitSeconds);
                _progressTrack.Visible = true;
            }
            else
            {
                _timeLabel.Text = $"{TrayApplicationContext.FormatTime(dailySeconds)} today  ·  {TrayApplicationContext.FormatTime(weeklySeconds)} this week";
                _progressTrack.Visible = false;
            }
        }
    }

    /// <summary>A slim, rounded, percentage-filled bar - green under 70%,
    /// yellow to 90%, red above, matching the same thresholds a user would
    /// intuitively expect from any usage/quota indicator.</summary>
    private class ProgressTrack : Panel
    {
        public double Percent { get; set; }

        public ProgressTrack()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var trackBrush = new SolidBrush(Theme.Well);
            using var trackPath = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Height / 2);
            e.Graphics.FillPath(trackBrush, trackPath);

            if (Percent <= 0) return;
            var fillColor = Percent < 0.7 ? Theme.AccentGreen : Percent < 0.9 ? Theme.AccentYellow : Theme.AccentRed;
            int fillWidth = Math.Max(Height, (int)(Width * Percent));
            using var fillBrush = new SolidBrush(fillColor);
            using var fillPath = Theme.RoundedRect(new Rectangle(0, 0, fillWidth, Height), Height / 2);
            e.Graphics.FillPath(fillBrush, fillPath);
        }
    }
}
