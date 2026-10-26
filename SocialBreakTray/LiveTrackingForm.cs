using static SocialBreakTray.Tr;
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
    private const int ContentWidth = 520;
    private const int InnerWidth = 480;
    private const int HeaderHeight = 88;
    private const int CardStep = 96;   // a card and the gap under it
    private const int MostCardsShown = 4;

    private readonly UsageAccumulator _accumulator;
    private readonly Func<List<MediaItemDto>> _getTrackedApps;
    private readonly Func<string?> _getCurrentlyTrackedUrl;
    private readonly Func<PlanDto?> _getPlan;
    private readonly int _resetHour;
    private readonly Action<MediaItemDto, string> _editLimit;

    private readonly FlowLayoutPanel _cardList;
    private readonly Dictionary<string, TrackedAppCard> _cards = new();
    private readonly Label _emptyLabel;
    private readonly Panel _divider;
    private readonly RoundedButton _addApps;
    private readonly DarkCheckBox _hideCheckbox;
    private int _laidOutFor = -1;
    private int _cardWidth = InnerWidth;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public LiveTrackingForm(UsageAccumulator accumulator, Func<List<MediaItemDto>> getTrackedApps,
        Func<string?> getCurrentlyTrackedUrl, Func<PlanDto?> getPlan, int resetHour, Action showAddApps,
        Action<MediaItemDto, string> editLimit)
        : base(T("Social Break - Live Tracking", "Social Break - Ζωντανή μέτρηση"), showMinimize: true)
    {
        _accumulator = accumulator;
        _getTrackedApps = getTrackedApps;
        _getCurrentlyTrackedUrl = getCurrentlyTrackedUrl;
        _getPlan = getPlan;
        _resetHour = resetHour;
        _editLimit = editLimit;

        var header = new Panel { Location = new Point(0, 0), Size = new Size(ContentWidth, HeaderHeight) };
        header.Paint += (_, e) =>
        {
            // Same brush and same width as the caption bar directly above,
            // so the horizontal gradient continues across the seam. No logo
            // here: the caption bar already carries it, and a second, bigger
            // one right under it only repeated it.
            using var gradient = Theme.CreateHeaderBrush(header.ClientRectangle);
            e.Graphics.FillRectangle(gradient, header.ClientRectangle);
        };

        var title = new Label
        {
            Text = T("Today", "Σήμερα"),
            ForeColor = Theme.TextPrimary,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 16),
            BackColor = Color.Transparent,
        };
        var subtitle = new Label
        {
            Text = T("Your apps on this computer, against their limits.", "Οι εφαρμογές σου σε αυτόν τον υπολογιστή, απέναντι στα όριά τους."),
            ForeColor = Theme.TextSecondary,
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Location = new Point(21, 52),
            BackColor = Color.Transparent,
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        _cardList = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Location = new Point(20, HeaderHeight + 16),
            Size = new Size(InnerWidth, CardStep),
            BackColor = Theme.Bg,
        };

        _emptyLabel = new Label
        {
            Text = T("No apps from this computer on your list yet. Pick some with Add apps… on the tray icon.", "Δεν υπάρχουν ακόμα εφαρμογές του υπολογιστή στη λίστα σου. Διάλεξε με την Προσθήκη εφαρμογών… στο εικονίδιο κάτω δεξιά."),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(InnerWidth - 20, 0),
            Visible = false,
        };

        // A hairline above the footer, separating live data from the
        // static explanatory text under it.
        _divider = new Panel
        {
            Size = new Size(InnerWidth, 1),
            BackColor = Theme.Border,
        };

        // Where apps get added - it used to be only on the tray icon's menu,
        // so from here there was no sign it existed at all.
        _addApps = new RoundedButton
        {
            Text = T("+  Add apps", "+  Προσθήκη εφαρμογών"),
            Size = new Size(190, 38),
            BackColor = Theme.Card,
            ForeColor = Theme.TextPrimary,
            OutlineColor = Theme.Border,
            HoverColor = Theme.MenuHover,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        _addApps.Click += (_, _) => showAddApps();

        // Only affects whether this window auto-opens on launch (see
        // TrayApplicationContext.InitializeAsync) - opening it from the
        // tray menu or double-clicking the icon always shows it regardless,
        // since that's an explicit request, not the automatic popup.
        // DarkCheckBox rather than a stock CheckBox, whose system-drawn
        // glyph stays a bright light-theme square whatever colors it's
        // given (see DarkCheckBox). Explicitly sized, since it owner-draws.
        _hideCheckbox = new DarkCheckBox
        {
            Text = T("Don't show this automatically on startup", "Να μην εμφανίζεται αυτόματα στην εκκίνηση"),
            Size = new Size(360, 20),
            Checked = TokenStore.IsWelcomeHiddenOnStartup(),
        };
        _hideCheckbox.CheckedChanged += (_, _) => TokenStore.SetHideWelcomeOnStartup(_hideCheckbox.Checked);

        Content.Controls.AddRange(new Control[] { header, _cardList, _emptyLabel, _divider, _addApps, _hideCheckbox });

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

        LayOut(trackedApps.Count);
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
                var url = app.Url;
                card = new TrackedAppCard(url, app.Name);
                var thisCard = card;
                card.Opened += () =>
                {
                    var current = _getTrackedApps().FirstOrDefault(a => string.Equals(a.Url, url, StringComparison.OrdinalIgnoreCase));
                    if (current != null) _editLimit(current, thisCard.DisplayName);
                };
                card.SetWidth(_cardWidth);
                _cards[app.Url] = card;
                _cardList.Controls.Add(card);
            }

            // Shown as the one shared limit: this computer's time plus the others'.
            int daily = _accumulator.DailySeconds.GetValueOrDefault(app.Url) + LimitEvaluator.ElsewhereSeconds(app.Url, plan, today: true);
            int weekly = _accumulator.WeeklySeconds.GetValueOrDefault(app.Url) + LimitEvaluator.ElsewhereSeconds(app.Url, plan, today: false);
            int dailyLimit = LimitEvaluator.GetDailyLimitSeconds(app.Url, plan, _resetHour);
            bool isLive = string.Equals(app.Url, currentUrl, StringComparison.OrdinalIgnoreCase);
            bool blocked = LimitEvaluator.IsBlockedToday(app.Url, plan, _resetHour);
            card.UpdateData(daily, weekly, dailyLimit, isLive, blocked);
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

    /// <summary>As tall as its apps, up to four, then the list scrolls -
    /// rather than a fixed window that was mostly empty space for one app.</summary>
    private void LayOut(int count)
    {
        if (count == _laidOutFor) return;
        _laidOutFor = count;

        // Exactly as tall as its cards, so Windows never adds scrollbars to a
        // list that fits; past four it scrolls, up and down only.
        bool scrolls = count > MostCardsShown;
        _cardList.AutoScroll = scrolls;
        if (scrolls)
        {
            _cardList.HorizontalScroll.Maximum = 0;
            _cardList.HorizontalScroll.Visible = false;
        }
        int cardWidth = scrolls ? InnerWidth - SystemInformation.VerticalScrollBarWidth - 4 : InnerWidth;
        foreach (var card in _cards.Values) card.SetWidth(cardWidth);
        _cardWidth = cardWidth;

        int listHeight = count == 0 ? 56 : Math.Min(count, MostCardsShown) * CardStep;
        _cardList.Height = listHeight;
        int y = _cardList.Bottom + 6;
        _divider.Location = new Point(20, y);
        _addApps.Location = new Point(20, y + 20);
        _hideCheckbox.Location = new Point(20, y + 20 + 38 + 18);
        SetContentSize(ContentWidth, y + 20 + 38 + 18 + 20 + 24);
    }

    /// <summary>Minutes, the way the phone and the website write them: 12m,
    /// 1h, 1h 5m. Seconds ticking by only made the numbers busy.</summary>
    private static string Short(int seconds)
    {
        int h = seconds / 3600;
        int m = (seconds % 3600) / 60;
        if (h == 0) return $"{m}{M}";
        return m == 0 ? $"{h}{H}" : $"{h}{H} {m}{M}";
    }

    /// <summary>One tracked app's row - an avatar badge, name, a LIVE badge
    /// when active, and either a progress bar (when a real daily limit
    /// applies) or plain totals otherwise. Kept alive and updated in place
    /// across refresh ticks rather than rebuilt, both for smoothness and so
    /// the daily-limit-relative progress bar doesn't need to be recreated
    /// every second.</summary>
    private class TrackedAppCard : Panel
    {
        private const int IconSize = 32;
        private string _appName;
        public string DisplayName => _appName;
        /// <summary>Clicked anywhere on the card: open its limit.</summary>
        public event Action? Opened;
        private Bitmap? _icon;
        private readonly Label _nameLabel;
        private readonly Label _chevron;
        private readonly Label _liveLabel;
        private readonly Label _timeLabel;
        private readonly ProgressTrack _progressTrack;

        public TrackedAppCard(string exe, string appName)
        {
            _appName = appName;
            Size = new Size(InnerWidth, 84);
            Margin = new Padding(0, 0, 0, 10);
            BackColor = Theme.Card;
            Region = new Region(Theme.RoundedRect(new Rectangle(0, 0, Width, Height), 12));

            var avatar = new Panel
            {
                Size = new Size(40, 40),
                Location = new Point(18, 22),
                BackColor = Color.Transparent,
            };
            avatar.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (_icon != null)
                {
                    e.Graphics.DrawImage(_icon, (40 - IconSize) / 2, (40 - IconSize) / 2, IconSize, IconSize);
                    return;
                }
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
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(72, 17),
                BackColor = Color.Transparent,
            };

            _liveLabel = new Label
            {
                Text = T("●  LIVE", "●  ΖΩΝΤΑΝΑ"),
                ForeColor = Theme.AccentGreen,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                AutoSize = true,
                BackColor = Color.Transparent,
                Visible = false,
            };
            _liveLabel.Location = new Point(Width - 20 - _liveLabel.PreferredWidth, 19);

            _timeLabel = new Label
            {
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(72, 40),
                BackColor = Color.Transparent,
            };

            _progressTrack = new ProgressTrack
            {
                Location = new Point(72, 64),
                Size = new Size(InnerWidth - 92 - 22, 6),
                Visible = false,
            };

            // "›" says the card opens; the whole card is the button.
            _chevron = new Label
            {
                Text = "›",
                ForeColor = Theme.TextMuted,
                Font = new Font("Segoe UI", 16f),
                AutoSize = true,
                BackColor = Color.Transparent,
            };
            Controls.AddRange(new Control[] { avatar, _nameLabel, _liveLabel, _timeLabel, _progressTrack, _chevron });
            Cursor = Cursors.Hand;
            foreach (Control control in Controls) control.Cursor = Cursors.Hand;
            Click += (_, _) => Opened?.Invoke();
            foreach (Control control in Controls) control.Click += (_, _) => Opened?.Invoke();
            PlaceRightSide();

            // The real name and icon, once found - the list may only know the
            // program as "discord.exe".
            Task.Run(() => AppLook.For(exe, IconSize)).ContinueWith(found =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                var (name, icon) = found.Result;
                BeginInvoke(() =>
                {
                    if (!string.IsNullOrWhiteSpace(name) && appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        _appName = name;
                        _nameLabel.Text = name;
                    }
                    _icon = icon;
                    avatar.Invalidate();
                });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        public void SetWidth(int width)
        {
            if (Width == width) return;
            Width = width;
            Region = new Region(Theme.RoundedRect(new Rectangle(0, 0, Width, Height), 12));
            _progressTrack.Width = Width - 92 - 22;
            PlaceRightSide();
        }

        private void PlaceRightSide()
        {
            _chevron.Location = new Point(Width - 14 - _chevron.PreferredWidth, (Height - _chevron.PreferredHeight) / 2 - 2);
            _liveLabel.Location = new Point(_chevron.Left - 10 - _liveLabel.PreferredWidth, 19);
        }

        public void UpdateData(int dailySeconds, int weeklySeconds, int dailyLimitSeconds, bool isLive, bool blocked)
        {
            BackColor = isLive ? Theme.CardLive : Theme.Card;
            _liveLabel.Visible = isLive;

            if (blocked)
            {
                _timeLabel.Text = T("Blocked today", "Μπλοκαρισμένη σήμερα");
                _progressTrack.Percent = 1.0;
                _progressTrack.Visible = true;
                _progressTrack.Invalidate();
                return;
            }

            if (dailyLimitSeconds > 0)
            {
                _timeLabel.Text = T($"{Short(dailySeconds)} of {Short(dailyLimitSeconds)} today  ·  {Short(weeklySeconds)} this week",
                    $"{Short(dailySeconds)} από {Short(dailyLimitSeconds)} σήμερα  ·  {Short(weeklySeconds)} αυτή την εβδομάδα");
                _progressTrack.Percent = Math.Min(1.0, (double)dailySeconds / dailyLimitSeconds);
                _progressTrack.Visible = true;
            }
            else
            {
                _timeLabel.Text = T($"{Short(dailySeconds)} today  ·  {Short(weeklySeconds)} this week",
                    $"{Short(dailySeconds)} σήμερα  ·  {Short(weeklySeconds)} αυτή την εβδομάδα");
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
