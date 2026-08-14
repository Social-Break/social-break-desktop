using SocialBreakTray.Api;
using SocialBreakTray.Auth;
using SocialBreakTray.Enforcement;
using SocialBreakTray.Onboarding;
using SocialBreakTray.Tracking;

namespace SocialBreakTray;

/// <summary>
/// The entire UI surface of this app - a tray icon and its menu, nothing
/// else. No main window is ever shown; this is deliberately "like a
/// switch," matching the browser extension's minimal popup rather than
/// trying to be a second dashboard. Every setting beyond "start with
/// Windows" and "pause briefly" lives on the website.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    // Not yet user-configurable in this version (see UsageAccumulator's
    // docstring) - matches the browser extension's own default resetHour.
    private const int ResetHour = 3;
    private const int HeartbeatIntervalMs = 5000;
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
    private readonly Dictionary<string, DateTime> _snoozeExemptions = new();
    // Apps that have already shown the block overlay at least once today -
    // switches BlockForm from its first-time "you've hit your limit" copy
    // to a "still want to continue?" framing on every re-encounter (after a
    // snooze expires, or after Close Program + reopening the app). Reset
    // implicitly by an app restart, same granularity as _snoozeExemptions.
    private readonly HashSet<string> _alreadyBlockedToday = new();
    // Apps where the user clicked "Continue nonetheless" on a repeat prompt -
    // stops the overlay from reappearing for that app for the rest of the
    // day. Usage still accrues and still reports to the server as normal;
    // this only suppresses the nag, not the tracking itself.
    private readonly HashSet<string> _dismissedForToday = new();
    private DateTime? _pausedUntilUtc;
    private BlockForm? _activeBlockForm;

    private ToolStripMenuItem _statusMenuItem = new();
    private ToolStripMenuItem _startWithWindowsItem = new();

    public TrayApplicationContext()
    {
        _trayIcon = new NotifyIcon
        {
            // Reads back the icon the .csproj's <ApplicationIcon> already
            // compiled into this exe, rather than shipping app.ico as a
            // second loose file next to a single-file publish output.
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath) ?? SystemIcons.Application,
            Visible = true,
            Text = "Social Break - not logged in",
        };
        _trayIcon.ContextMenuStrip = BuildMenu();
        _trayIcon.DoubleClick += (_, _) => OpenDashboard();

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
        var startupTimer = new System.Windows.Forms.Timer { Interval = 1 };
        startupTimer.Tick += async (_, _) =>
        {
            startupTimer.Stop();
            startupTimer.Dispose();
            await InitializeAsync();
        };
        startupTimer.Start();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _statusMenuItem = new ToolStripMenuItem("Social Break") { Enabled = false };
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Pause tracking (5 min)", null, (_, _) => PauseTracking());
        menu.Items.Add("Open Dashboard", null, (_, _) => OpenDashboard());
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
        // minimal. The user can opt out of this automatic showing via its
        // checkbox; opening it from the tray menu (ShowAbout() called
        // directly, bypassing this check) always shows it regardless of
        // that preference, since that's an explicit request.
        if (!TokenStore.IsWelcomeHiddenOnStartup())
        {
            ShowAbout();
        }
    }

    private static void ShowAbout()
    {
        using var about = new AboutForm(TokenStore.IsWelcomeHiddenOnStartup());
        about.ShowDialog();
        TokenStore.SetHideWelcomeOnStartup(about.HideOnStartup);
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

        if (_snoozeExemptions.TryGetValue(match.Url, out var snoozedUntil) && DateTime.UtcNow < snoozedUntil)
        {
            SetTrayStatus($"Snoozed - {match.Name}");
            return;
        }

        _accumulator.AddSeconds(match.Url, HeartbeatIntervalMs / 1000);

        int dailySeconds = _accumulator.DailySeconds.GetValueOrDefault(match.Url);
        int weeklySeconds = _accumulator.WeeklySeconds.GetValueOrDefault(match.Url);
        SetTrayStatus($"{match.Name} - {FormatTime(weeklySeconds)} this week");

        if (!_dismissedForToday.Contains(match.Url))
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

        bool isRepeat = _alreadyBlockedToday.Contains(match.Url);
        _alreadyBlockedToday.Add(match.Url);

        var block = new BlockForm(match.Name, windowHandle, blockReason, isRepeat);
        block.ContinueRequested += permanent =>
        {
            if (permanent)
            {
                _dismissedForToday.Add(match.Url);
            }
            else
            {
                _snoozeExemptions[match.Url] = DateTime.UtcNow.AddMinutes(5);
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

    private static void OpenDashboard()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://socialbreak.onrender.com/",
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

    private static string FormatTime(int totalSeconds)
    {
        int h = totalSeconds / 3600;
        int m = (totalSeconds % 3600) / 60;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}
