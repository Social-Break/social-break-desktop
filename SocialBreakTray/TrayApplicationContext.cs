using SocialBreakTray.Api;
using SocialBreakTray.Auth;
using SocialBreakTray.Enforcement;
using SocialBreakTray.Onboarding;
using SocialBreakTray.Tracking;
using SocialBreakTray.Ui;

namespace SocialBreakTray;

/// <summary>
/// The tray icon, its menu, and an optional Live Tracking window
/// (LiveTrackingForm) showing today's/this week's accrued time per app -
/// nothing else. That window is deliberately read-only: no limits, rules,
/// or Media List editing happen here. Every setting beyond "start with
/// Windows," "pause briefly," and viewing live totals still lives on the
/// website.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    // Not yet user-configurable in this version (see UsageAccumulator's
    // docstring) - matches the browser extension's own default resetHour.
    private const int ResetHour = 3;
    private const int HeartbeatIntervalMs = 1000;
    private const int SyncIntervalMs = 10 * 60 * 1000; // 10 minutes, matches the extension's reportUsage alarm

    private readonly NotifyIcon _trayIcon;
    private readonly SocialBreakApiClient _apiClient = new();
    private readonly IForegroundWindowProvider _foregroundProvider = new Win32ForegroundWindowProvider();
    private readonly IIdleTimeProvider _idleProvider = new Win32IdleTimeProvider();
    private readonly UsageAccumulator _accumulator = new(ResetHour);
    private UsageReporter? _usageReporter;

    private readonly System.Windows.Forms.Timer _heartbeatTimer;
    private readonly System.Windows.Forms.Timer _syncTimer;

    private List<MediaItemDto> _trackedApps = new();
    private PlanDto? _plan;
    // Persisted to disk (see BlockStateStore's docstring) rather than kept
    // purely in memory - a process restart shouldn't make the app "forget"
    // an already-shown-today or already-dismissed-today choice.
    private readonly BlockStateStore _blockState = new(ResetHour);
    private DateTime? _pausedUntilUtc;
    private BlockForm? _activeBlockForm;
    private LiveTrackingForm? _liveTrackingForm;
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
            Text = "Social Break - not logged in",
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

        menu.Items.Add("Pause tracking (5 min)", null, (_, _) => PauseTracking());
        menu.Items.Add("Live Tracking", null, (_, _) => ShowLiveTracking());
        menu.Items.Add("Open Website", null, (_, _) => OpenWebsite());
        menu.Items.Add("About Social Break", null, (_, _) => ShowAbout());

        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = AutoStart.IsEnabled() };
        _startWithWindowsItem.Click += (_, _) => AutoStart.SetEnabled(_startWithWindowsItem.Checked);
        menu.Items.Add(_startWithWindowsItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Log Out", null, (_, _) => LogOut());
        menu.Items.Add("Quit", null, (_, _) => ExitApp());

        return menu;
    }

    private async Task InitializeAsync()
    {
        var token = TokenStore.LoadToken();
        if (token == null)
        {
            if (!await ShowDisclosureIfNeededAsync()) { ExitApp(); return; }
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
        if (!TokenStore.IsWelcomeHiddenOnStartup())
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

    private Task<bool> ShowDisclosureIfNeededAsync()
    {
        if (TokenStore.IsDisclosureAcknowledged()) return Task.FromResult(true);

        using var disclosure = new DisclosureForm();
        if (disclosure.ShowDialog() != DialogResult.OK) return Task.FromResult(false);
        TokenStore.MarkDisclosureAcknowledged();
        return Task.FromResult(true);
    }

    private bool ShowLoginAndStoreToken()
    {
        using var login = new LoginForm(_apiClient);
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
        catch
        {
            // Network hiccup - keep using whatever was cached from the last
            // successful sync, try again next cycle.
        }

        if (_usageReporter != null)
        {
            await _usageReporter.ReportAsync();
        }
    }

    private void HeartbeatTick()
    {
        // Assumed not-tracking unless the accrual path below is actually
        // reached this tick - simpler and less error-prone than clearing it
        // at every one of the several early-return points above that path.
        _currentlyTrackingUrl = null;

        if (_pausedUntilUtc is { } pausedUntil)
        {
            if (DateTime.UtcNow < pausedUntil)
            {
                SetTrayStatus("Paused");
                return;
            }
            _pausedUntilUtc = null;
        }

        if (_idleProvider.GetIdleTime() >= TimeSpan.FromSeconds(60))
        {
            SetTrayStatus("Idle");
            return;
        }

        var foreground = _foregroundProvider.GetForegroundProcess();
        if (foreground == null)
        {
            SetTrayStatus("Active");
            return;
        }

        var match = _trackedApps.FirstOrDefault(m => string.Equals(m.Url, foreground.Value.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            SetTrayStatus("Active");
            return;
        }

        if (_blockState.IsSnoozed(match.Url))
        {
            SetTrayStatus($"Snoozed - {match.Name}");
            return;
        }

        _accumulator.AddSeconds(match.Url, HeartbeatIntervalMs / 1000);
        _currentlyTrackingUrl = match.Url;

        int dailySeconds = _accumulator.DailySeconds.GetValueOrDefault(match.Url);
        int weeklySeconds = _accumulator.WeeklySeconds.GetValueOrDefault(match.Url);
        SetTrayStatus($"{match.Name} - {FormatTime(weeklySeconds)} this week");

        if (!_blockState.IsDismissedForToday(match.Url))
        {
            var blockReason = LimitEvaluator.IsLimitReached(match.Url, _plan, dailySeconds, weeklySeconds, ResetHour);
            if (blockReason != null)
            {
                ShowBlockOverlay(match, foreground.Value.WindowHandle, blockReason);
            }
        }
    }

    private void ShowBlockOverlay(MediaItemDto match, nint windowHandle, string blockReason)
    {
        if (_activeBlockForm is { IsDisposed: false }) return; // one at a time

        bool isRepeat = _blockState.HasAlreadyBlockedToday(match.Url);
        _blockState.MarkBlockedToday(match.Url);

        var block = new BlockForm(match.Name, windowHandle, blockReason, isRepeat);
        block.ContinueRequested += permanent =>
        {
            if (permanent)
            {
                _blockState.MarkDismissedForToday(match.Url);
            }
            else
            {
                _blockState.Snooze(match.Url, TimeSpan.FromMinutes(5));
            }
        };
        block.FormClosed += (_, _) => _activeBlockForm = null;
        _activeBlockForm = block;
        block.Show();
    }

    private void PauseTracking()
    {
        _pausedUntilUtc = DateTime.UtcNow.AddMinutes(5);
        SetTrayStatus("Paused");
    }

    /// <summary>Opens the Live Tracking window, or brings the existing one
    /// back to the front if it's already open.</summary>
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

        _liveTrackingForm = new LiveTrackingForm(_accumulator, () => _trackedApps, () => _currentlyTrackingUrl, () => _plan, ResetHour);
        _liveTrackingForm.FormClosed += (_, _) => _liveTrackingForm = null;
        _liveTrackingForm.Show();
        _liveTrackingForm.Activate();
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
        return h > 0 ? $"{h}h {m}m" : $"{m}m {s}s";
    }
}
