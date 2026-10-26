using static SocialBreakTray.Tr;
using SocialBreakTray.Api;
using SocialBreakTray.Auth;
using SocialBreakTray.Enforcement;
using SocialBreakTray.Tracking;
using SocialBreakTray.Ui;

namespace SocialBreakTray;

/// <summary>
/// The tray icon, its menu, an optional Live Tracking window
/// (LiveTrackingForm) showing today's/this week's accrued time per app, and
/// the "Add apps" window (AddAppsForm) for putting this computer's programs
/// on the list. Adding is the only change to the list made here: removing,
/// limits and rules - every setting beyond "start with Windows," "pause
/// briefly" and the two windows - still live on the website.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    // A day runs midnight to midnight, as on the phone and in the browser
    // extension, so a late session counts toward the same day everywhere.
    private const int ResetHour = 0;
    private const int HeartbeatIntervalMs = 1000;
    private const int SyncIntervalMs = 10 * 60 * 1000; // 10 minutes, matches the extension's reportUsage alarm

    private readonly NotifyIcon _trayIcon;
    private readonly SocialBreakApiClient _apiClient = new();
    private readonly IForegroundWindowProvider _foregroundProvider = new Win32ForegroundWindowProvider();
    private readonly IIdleTimeProvider _idleProvider = new Win32IdleTimeProvider();
    private readonly UsageAccumulator _accumulator = new(ResetHour);
    // The week's most-used programs, for the website to offer by name.
    private readonly TopAppsTracker _topApps = new();
    private readonly Dictionary<string, DateTime> _closingUntil = new(StringComparer.OrdinalIgnoreCase);
    // Read once, not every second: the setting lives in the encrypted config.
    private bool _suggestApps = TokenStore.IsSuggestingApps();
    private UsageReporter? _usageReporter;

    private readonly System.Windows.Forms.Timer _heartbeatTimer;
    private readonly System.Windows.Forms.Timer _syncTimer;

    private List<MediaItemDto> _trackedApps = new();
    private PlanDto? _plan;
    // Persisted to disk (see BlockStateStore's docstring) rather than kept
    // purely in memory - a process restart shouldn't make the app "forget"
    // an already-shown-today or already-dismissed-today choice.
    private readonly BlockStateStore _blockState = new(ResetHour);
    private BlockForm? _activeBlockForm;
    private LiveTrackingForm? _liveTrackingForm;
    private AddAppsForm? _addAppsForm;
    // The identifier of whichever tracked app is actively accruing time
    // right now (null when idle/paused/no match) - read live by
    // LiveTrackingForm via a getter delegate, not pushed to it, so it stays
    // correct even while that window isn't open.
    private string? _currentlyTrackingUrl;

    private ToolStripMenuItem _statusMenuItem = new();
    private ToolStripMenuItem _startWithWindowsItem = new();

    // Set once login/sync have completed - see OnShowRequestedByOtherInstance
    // for why a relaunch must not pop the dashboard before then.
    private bool _initialized;

    // How a second launch of the exe (double-clicking the desktop or Start
    // menu shortcut while the app is already in the tray) asks the running
    // instance to surface its window. See Program.cs, which signals this
    // instead of just exiting.
    private readonly EventWaitHandle _showRequest =
        new(false, EventResetMode.AutoReset, Program.ShowRequestEventName);

    // A handle-owning control created on the UI thread, purely so the
    // waiter thread below has something to marshal back through - an
    // ApplicationContext has no window of its own to BeginInvoke on.
    private readonly Control _uiMarshal = new();

    public TrayApplicationContext()
    {
        _trayIcon = new NotifyIcon
        {
            // The full multi-frame icon (see Theme), so the shell picks the
            // real 16x16 frame for the notification area. The previous
            // ExtractAssociatedIcon call returned only a 32x32 frame, which
            // the tray then squashed to 16 - visibly soft next to every
            // other tray icon.
            Icon = Theme.GetIcon(SystemInformation.SmallIconSize.Width) ?? SystemIcons.Application,
            Visible = true,
            Text = T("Social Break - not logged in", "Social Break - χωρίς σύνδεση"),
        };
        _trayIcon.ContextMenuStrip = BuildMenu();
        _trayIcon.DoubleClick += (_, _) => ShowLiveTracking();

        _heartbeatTimer = new System.Windows.Forms.Timer { Interval = HeartbeatIntervalMs };
        _heartbeatTimer.Tick += (_, _) => HeartbeatTick();

        _syncTimer = new System.Windows.Forms.Timer { Interval = SyncIntervalMs };
        _syncTimer.Tick += async (_, _) => await SyncAsync();

        // Deferred via a one-shot timer rather than kicked off directly from
        // the constructor: this constructor runs before Application.Run()
        // starts pumping messages (it's still evaluating Program.cs's `new
        // TrayApplicationContext()` argument), so an Application.Exit() call
        // from a cancelled disclosure/login (see ExitApp()) would have no
        // running message loop to terminate. A Timer only ticks once a
        // message loop is actually pumping, which guarantees
        // InitializeAsync() - and any exit it triggers - runs after
        // Application.Run() is live.
        StartShowRequestListener();

        var startupTimer = new System.Windows.Forms.Timer { Interval = 1 };
        startupTimer.Tick += async (_, _) =>
        {
            startupTimer.Stop();
            startupTimer.Dispose();
            await InitializeAsync();
        };
        startupTimer.Start();
    }

    /// <summary>Waits for a second instance to ask us to show ourselves.
    /// A blocking background thread rather than a polling timer, so the
    /// window comes up immediately on the double-click rather than up to a
    /// tick later.</summary>
    private void StartShowRequestListener()
    {
        _ = _uiMarshal.Handle; // force handle creation while we're on the UI thread

        var listener = new Thread(() =>
        {
            while (true)
            {
                _showRequest.WaitOne();
                try
                {
                    _uiMarshal.BeginInvoke(OnShowRequestedByOtherInstance);
                }
                catch (ObjectDisposedException)
                {
                    return; // app is shutting down
                }
                catch (InvalidOperationException)
                {
                    return; // handle already gone
                }
            }
        })
        {
            IsBackground = true,
            Name = "SocialBreak show-request listener",
        };
        listener.Start();
    }

    private void OnShowRequestedByOtherInstance()
    {
        // Ignored until the app is past disclosure/login. Those are modal
        // dialogs, and opening a modeless window behind one would strand it
        // unreachable until the dialog is dealt with.
        if (!_initialized) return;
        ShowLiveTracking();
    }

    private ContextMenuStrip BuildMenu()
    {
        // Themed rather than left on the WinForms default: the tray menu is
        // the surface a user touches every time they interact with this app,
        // and a white Office-2003 menu hanging off a dark app is the most
        // visible seam it has. See DarkMenu.
        var menu = DarkMenu.Apply(new ContextMenuStrip());

        _statusMenuItem = new ToolStripMenuItem("Social Break") { Enabled = false };
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        // No "pause tracking": a limit that steps aside on request isn't one.
        menu.Items.Add(T("Dashboard", "Πίνακας"), null, (_, _) => ShowLiveTracking());
        menu.Items.Add(T("Add apps…", "Προσθήκη εφαρμογών…"), null, (_, _) => ShowAddApps());
        menu.Items.Add(T("Open Website", "Άνοιγμα ιστότοπου"), null, (_, _) => OpenWebsite());
        menu.Items.Add(T("About Social Break", "Σχετικά με το Social Break"), null, (_, _) => ShowAbout());

        _startWithWindowsItem = new ToolStripMenuItem(T("Start with Windows", "Έναρξη με τα Windows")) { CheckOnClick = true, Checked = AutoStart.IsEnabled() };
        _startWithWindowsItem.Click += (_, _) => AutoStart.SetEnabled(_startWithWindowsItem.Checked);
        menu.Items.Add(_startWithWindowsItem);

        // The most-used programs, sent so the website can offer them - off
        // stops counting and clears what was sent.
        var suggestItem = new ToolStripMenuItem(T("Suggest my most-used apps", "Πρότεινε τις εφαρμογές που χρησιμοποιώ")) { CheckOnClick = true, Checked = _suggestApps };
        suggestItem.Click += async (_, _) =>
        {
            _suggestApps = suggestItem.Checked;
            TokenStore.SetSuggestingApps(_suggestApps);
            if (suggestItem.Checked) return;
            _topApps.Clear();
            try { await _apiClient.PostTopAppsAsync(Array.Empty<TopApp>()); } catch { }
        };
        menu.Items.Add(suggestItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Log Out", "Αποσύνδεση"), null, (_, _) => LogOut());
        menu.Items.Add(T("Quit", "Έξοδος"), null, (_, _) => ExitApp());

        return menu;
    }

    private async Task InitializeAsync()
    {
        var token = TokenStore.LoadToken();
        // Signing in now, rather than already signed in from before.
        bool justConnected = token == null;
        if (token == null)
        {
            if (!ShowLoginAndStoreToken()) { ExitApp(); return; }
            token = TokenStore.LoadToken();
        }

        if (token == null) { ExitApp(); return; }

        _apiClient.SetToken(token);
        _usageReporter = new UsageReporter(_apiClient, _accumulator);

        await SyncAsync();

        _heartbeatTimer.Start();
        _syncTimer.Start();

        // Shown on every launch by default - without it, a background-only
        // process gives zero visible feedback that it launched or what it
        // does, which reads as broken/amateur rather than intentionally
        // minimal. Live Tracking (not the plain About dialog) is the one
        // that auto-opens, since it gives real, useful information rather
        // than just a static explanation. The user can opt out of this
        // automatic showing via its own checkbox; opening it from the tray
        // menu or double-clicking the icon always shows it regardless of
        // that preference, since that's an explicit request.
        //
        // Just connected with nothing from this computer on the list yet:
        // the first thing to do is pick the apps, so that window comes up
        // instead.
        if (justConnected && _trackedApps.Count == 0)
        {
            ShowAddApps();
        }
        else if (!TokenStore.IsWelcomeHiddenOnStartup())
        {
            ShowLiveTracking();
        }

        _initialized = true;
    }

    private static void ShowAbout()
    {
        using var about = new AboutForm();
        about.ShowDialog();
    }

    private bool ShowLoginAndStoreToken()
    {
        // The welcome comes first on a first run (LoginForm.BuildWelcome).
        using var login = new LoginForm(_apiClient, showWelcome: !TokenStore.IsDisclosureAcknowledged());
        if (login.ShowDialog() != DialogResult.OK || login.AcquiredToken == null) return false;
        TokenStore.SaveToken(login.AcquiredToken);
        return true;
    }

    private async Task SyncAsync()
    {
        try
        {
            var allMedia = await _apiClient.GetMediaItemsAsync();
            _trackedApps = allMedia.Where(m => m.SourceType == "desktop_app" && m.IsActive).ToList();
            _plan = await _apiClient.GetPlanAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Signed out on the website ("Sign out everywhere"), or the account
            // is gone: the token opens nothing any more. Say so and ask to
            // sign in again, rather than carry on enforcing rules nobody can
            // see or change from here.
            SignedOut();
            return;
        }
        catch
        {
            // Network hiccup - keep using whatever was cached from the last
            // successful sync, try again next cycle.
        }

        if (_usageReporter != null)
        {
            await _usageReporter.ReportAsync();
        }

        // The week's most-used programs, when they've changed.
        if (_suggestApps)
        {
            _topApps.Save();
            if (_topApps.ChangedTop() is { } top)
            {
                try { await _apiClient.PostTopAppsAsync(top); } catch { }
            }
        }
    }

    private void HeartbeatTick()
    {
        // Assumed not-tracking unless the accrual path below is actually
        // reached this tick - simpler and less error-prone than clearing it
        // at every one of the several early-return points above that path.
        _currentlyTrackingUrl = null;

        if (_idleProvider.GetIdleTime() >= TimeSpan.FromSeconds(60))
        {
            SetTrayStatus(T("Idle", "Αδράνεια"));
            return;
        }

        var foreground = _foregroundProvider.GetForegroundProcess();
        if (foreground == null)
        {
            SetTrayStatus(T("Active", "Ενεργό"));
            return;
        }

        if (_suggestApps) _topApps.Add(foreground.Value.ProcessName, foreground.Value.FullPath, HeartbeatIntervalMs / 1000);

        var match = _trackedApps.FirstOrDefault(m => string.Equals(m.Url, foreground.Value.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            SetTrayStatus(T("Active", "Ενεργό"));
            return;
        }

        // No way round a block: a lock, or strict mode.
        bool locked = LimitEvaluator.IsLocked(_plan) || _plan?.StrictMode == true;
        if (!locked && _blockState.IsSnoozed(match.Url))
        {
            SetTrayStatus(T($"Snoozed - {match.Name}", $"Σε αναβολή - {match.Name}"));
            return;
        }

        _accumulator.AddSeconds(match.Url, HeartbeatIntervalMs / 1000);
        _currentlyTrackingUrl = match.Url;

        int dailySeconds = _accumulator.DailySeconds.GetValueOrDefault(match.Url);
        int weeklySeconds = _accumulator.WeeklySeconds.GetValueOrDefault(match.Url);
        SetTrayStatus(T($"{match.Name} - {FormatTime(weeklySeconds)} this week", $"{match.Name} - {FormatTime(weeklySeconds)} αυτή την εβδομάδα"));

        // Once an app's time is up it stays up: only a snooze (three a day)
        // steps aside, and never in a lock or strict mode.
        var blockReason = LimitEvaluator.IsLimitReached(match.Url, _plan, dailySeconds, weeklySeconds, ResetHour);
        bool closing = _closingUntil.TryGetValue(match.Url, out var closingUntil) && DateTime.UtcNow < closingUntil;
        if (blockReason != null && !closing)
        {
            ShowBlockOverlay(match, foreground.Value.WindowHandle, blockReason);
        }
    }

    private void ShowBlockOverlay(MediaItemDto match, nint windowHandle, string blockReason)
    {
        if (_activeBlockForm is { IsDisposed: false }) return; // one at a time

        bool canSnooze = blockReason != LimitEvaluator.BlockReason.Locked && _plan?.StrictMode != true && _blockState.HasSnoozeLeft;
        string? sealedNote = blockReason == LimitEvaluator.BlockReason.Locked || canSnooze ? null
            : _plan?.StrictMode == true ? T("Strict mode is on - no snoozes. It stays closed.", "Η αυστηρή λειτουργία είναι ενεργή - χωρίς αναβολές. Μένει κλειστό.")
            : T("No snoozes left today - it stays closed until tomorrow.", "Δεν μένουν αναβολές για σήμερα - μένει κλειστό μέχρι αύριο.");
        var block = new BlockForm(match.Name, windowHandle, blockReason, canSnooze, _plan?.LockedUntil, sealedNote);
        block.SnoozeRequested += () => _blockState.Snooze(match.Url, TimeSpan.FromMinutes(5));
        // Closing an app can take a moment - Vivaldi asks "Do you really want
        // to exit?" - and covering that question again a second later made the
        // app impossible to close. A short hold-off lets it be answered; if the
        // app is still open after it, the block comes back.
        block.CloseProgramRequested += () => _closingUntil[match.Url] = DateTime.UtcNow.AddSeconds(20);
        block.FormClosed += (_, _) => _activeBlockForm = null;
        _activeBlockForm = block;
        block.Show();
    }

    /// <summary>Opens the Live Tracking window, or brings the existing one
    /// back to the front if it's already open.</summary>
    /// <summary>One app's daily limit, from its card in Live Tracking; a
    /// sync after saving, so the card and the blocking use it straight away.</summary>
    private async void EditLimit(MediaItemDto app, string displayName)
    {
        using var form = new LimitForm(_apiClient, app, displayName, _plan);
        if (form.ShowDialog(_liveTrackingForm) == DialogResult.OK) await SyncAsync();
    }

    private void ShowLiveTracking()
    {
        if (_liveTrackingForm is { IsDisposed: false } existing)
        {
            // Activate() on its own does nothing to a minimized window - it
            // moves focus, it doesn't restore. So every "open the dashboard"
            // gesture (tray double-click, the menu item, relaunching the
            // shortcut) silently no-opped once the window had been minimized,
            // which reads as the app being broken. WindowState has to be put
            // back explicitly first.
            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }
            if (!existing.Visible) existing.Show();
            existing.Activate();
            existing.BringToFront();
            return;
        }

        _liveTrackingForm = new LiveTrackingForm(_accumulator, () => _trackedApps, () => _currentlyTrackingUrl, () => _plan, ResetHour, ShowAddApps, EditLimit);
        _liveTrackingForm.FormClosed += (_, _) => _liveTrackingForm = null;
        _liveTrackingForm.Show();
        _liveTrackingForm.Activate();
    }

    /// <summary>Opens "Which apps do you want to limit?", or brings it back
    /// to the front. Whatever it adds is picked up straight away.</summary>
    private void ShowAddApps()
    {
        if (_addAppsForm is { IsDisposed: false } existing)
        {
            existing.Activate();
            existing.BringToFront();
            return;
        }

        var form = new AddAppsForm(_apiClient, _topApps.Top(), _trackedApps.Select(m => m.Url));
        form.FormClosed += async (_, _) =>
        {
            _addAppsForm = null;
            if (form.Added.Count == 0) return;
            await SyncAsync();
            var message = form.Added.Count == 1
                ? T($"{form.Added[0]} is on your list now.", $"{form.Added[0]}: τώρα στη λίστα σου.")
                : T($"{form.Added.Count} apps are on your list now.", $"{form.Added.Count} εφαρμογές είναι τώρα στη λίστα σου.");
            _trayIcon.ShowBalloonTip(5000, "Social Break", message, ToolTipIcon.None);
        };
        _addAppsForm = form;
        form.Show();
        form.Activate();
    }

    private static void OpenWebsite()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://social-break.com/",
                UseShellExecute = true, // required to open a URL via the default browser, not launch it as an exe
            });
        }
        catch
        {
            // If the default browser can't be launched for some reason,
            // there's nothing else useful to do here - the tray menu item
            // just silently no-ops rather than crashing the app.
        }
    }

    private void SignedOut()
    {
        _heartbeatTimer.Stop();
        _syncTimer.Stop();
        MessageBox.Show(
            T("You've been signed out of Social Break on this computer. Sign in again to keep your limits here.",
              "Αποσυνδέθηκες από το Social Break σε αυτόν τον υπολογιστή. Συνδέσου ξανά για να ισχύουν τα όριά σου εδώ."),
            "Social Break", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LogOut();
    }

    private void LogOut()
    {
        _heartbeatTimer.Stop();
        _syncTimer.Stop();
        TokenStore.ClearToken();
        _trayIcon.Visible = false;
        Application.Restart();
    }

    private void ExitApp()
    {
        _heartbeatTimer.Stop();
        _syncTimer.Stop();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private void SetTrayStatus(string status)
    {
        _statusMenuItem.Text = $"Social Break - {status}";
        // NotifyIcon.Text has a ~127-character OS-imposed limit.
        var tooltip = $"Social Break - {status}";
        _trayIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    internal static string FormatTime(int totalSeconds)
    {
        int h = totalSeconds / 3600;
        int m = (totalSeconds % 3600) / 60;
        int s = totalSeconds % 60;
        return h > 0 ? $"{h}{H} {m}{M}" : $"{m}{M} {s}{S}";
    }
}
