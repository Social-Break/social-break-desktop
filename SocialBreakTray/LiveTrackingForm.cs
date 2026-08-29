using System.Drawing.Drawing2D;
using SocialBreakTray.Api;
using SocialBreakTray.Auth;
using SocialBreakTray.Enforcement;
using SocialBreakTray.Tracking;

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
/// </summary>
public class LiveTrackingForm : Form
{
    private static readonly Color BgColor = Color.FromArgb(0x1e, 0x1e, 0x2e);
    private static readonly Color CardColor = Color.FromArgb(0x29, 0x2c, 0x3c);
    private static readonly Color CardLiveColor = Color.FromArgb(0x24, 0x35, 0x2b);
    private static readonly Color TextPrimary = Color.FromArgb(0xcd, 0xd6, 0xf4);
    private static readonly Color TextSecondary = Color.FromArgb(0xa6, 0xad, 0xc8);
    private static readonly Color TextMuted = Color.FromArgb(0x6c, 0x70, 0x86);
    private static readonly Color AccentGreen = Color.FromArgb(0xa6, 0xe3, 0xa1);
    private static readonly Color AccentYellow = Color.FromArgb(0xf9, 0xe2, 0xaf);
    private static readonly Color AccentRed = Color.FromArgb(0xf3, 0x8b, 0xa8);

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
    {
        _accumulator = accumulator;
        _getTrackedApps = getTrackedApps;
        _getCurrentlyTrackedUrl = getCurrentlyTrackedUrl;
        _getPlan = getPlan;
        _resetHour = resetHour;

        Text = "Social Break - Live Tracking";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 480);
        BackColor = BgColor;

        var header = new Panel { Location = new Point(0, 0), Size = new Size(460, 76) };
        header.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var gradient = new LinearGradientBrush(
                header.ClientRectangle,
                Color.FromArgb(0x20, 0x1c, 0x33),
                Color.FromArgb(0x2a, 0x22, 0x45),
                LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(gradient, header.ClientRectangle);

            var icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
            if (icon != null)
            {
                e.Graphics.DrawIcon(icon, new Rectangle(20, 18, 40, 40));
            }
        };

        var title = new Label
        {
            Text = "Live Tracking",
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(72, 16),
            BackColor = Color.Transparent,
        };
        var subtitle = new Label
        {
            Text = "Desktop apps counted against your Media List",
            ForeColor = TextSecondary,
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
            BackColor = BgColor,
        };

        _emptyLabel = new Label
        {
            Text = "No desktop apps on your Media List yet - add one on the website.",
            ForeColor = TextMuted,
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Visible = false,
        };

        var note = new Label
        {
            Text = "Limits, rules, and your Media List are managed on the website.",
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Location = new Point(20, 404),
        };

        // Only affects whether this window auto-opens on launch (see
        // TrayApplicationContext.InitializeAsync) - opening it from the
        // tray menu or double-clicking the icon always shows it regardless,
        // since that's an explicit request, not the automatic popup.
        var hideCheckbox = new CheckBox
        {
            Text = "Don't show this automatically on startup",
            ForeColor = TextSecondary,
            AutoSize = true,
            Location = new Point(20, 434),
            Checked = TokenStore.IsWelcomeHiddenOnStartup(),
        };
        hideCheckbox.CheckedChanged += (_, _) => TokenStore.SetHideWelcomeOnStartup(hideCheckbox.Checked);

        Controls.AddRange(new Control[] { header, _cardList, _emptyLabel, note, hideCheckbox });

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

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
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
            BackColor = CardColor;
            Region = new Region(RoundedRect(new Rectangle(0, 0, Width, Height), 12));

            var avatar = new Panel
            {
                Size = new Size(40, 40),
                Location = new Point(16, 18),
                BackColor = Color.Transparent,
            };
            avatar.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var bg = new SolidBrush(Color.FromArgb(0x45, 0x47, 0x5a));
                e.Graphics.FillEllipse(bg, 0, 0, 39, 39);
                var letter = _appName.Length > 0 ? _appName[..1].ToUpperInvariant() : "?";
                using var font = new Font("Segoe UI", 13, FontStyle.Bold);
                var size = e.Graphics.MeasureString(letter, font);
                e.Graphics.DrawString(letter, font, Brushes.White, (40 - size.Width) / 2, (40 - size.Height) / 2);
            };

            _nameLabel = new Label
            {
                Text = appName,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(68, 14),
                BackColor = Color.Transparent,
            };

            _liveLabel = new Label
            {
                Text = "●  LIVE",
                ForeColor = AccentGreen,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                AutoSize = true,
                BackColor = Color.Transparent,
                Visible = false,
            };
            _liveLabel.Location = new Point(Width - 20 - _liveLabel.PreferredWidth, 16);

            _timeLabel = new Label
            {
                ForeColor = TextSecondary,
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
            BackColor = isLive ? CardLiveColor : CardColor;
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
            using var trackBrush = new SolidBrush(Color.FromArgb(0x11, 0x11, 0x1b));
            using var trackPath = RoundedRect(new Rectangle(0, 0, Width, Height), Height / 2);
            e.Graphics.FillPath(trackBrush, trackPath);

            if (Percent <= 0) return;
            var fillColor = Percent < 0.7 ? AccentGreen : Percent < 0.9 ? AccentYellow : AccentRed;
            int fillWidth = Math.Max(Height, (int)(Width * Percent));
            using var fillBrush = new SolidBrush(fillColor);
            using var fillPath = RoundedRect(new Rectangle(0, 0, fillWidth, Height), Height / 2);
            e.Graphics.FillPath(fillBrush, fillPath);
        }
    }
}
