using System.Runtime.InteropServices;

namespace SocialBreakTray;

internal static class Program
{
    // A browser extension gets a one-service-worker-per-profile guarantee
    // for free; a plain .exe has no such guarantee, so a stray shortcut or
    // a user double-clicking it twice would otherwise spin up two trackers
    // independently racing each other's local accumulator file and server
    // writes. This named Mutex is the standard WinForms single-instance
    // pattern that prevents that.
    private const string MutexName = "Global\\SocialBreakTray-SingleInstance";

    /// <summary>Signalled by a second instance to ask the running one to
    /// bring its Live Tracking window up. Waited on in
    /// TrayApplicationContext.StartShowRequestListener.</summary>
    internal const string ShowRequestEventName = "Global\\SocialBreakTray-ShowWindow";

    // Windows blocks a background process from stealing the foreground. The
    // running instance is exactly that, so it can't raise its own window in
    // response to our signal unless we - the process the user just launched,
    // and which therefore holds the foreground right - hand that right over
    // first. Without this the window restores but stays behind whatever the
    // user was looking at.
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private const int AsfwAny = -1;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running. Rather than exiting silently - which made
            // double-clicking the desktop/Start shortcut of a running app do
            // nothing at all, indistinguishable from the app being broken -
            // ask the live instance to surface its dashboard, the same thing
            // double-clicking the tray icon does.
            SignalRunningInstance();
            return;
        }

        // Classic explicit initialization rather than the SDK's
        // auto-generated ApplicationConfiguration.Initialize() - equally
        // correct, and doesn't depend on MSBuild's WinForms source
        // generation having fired the way `dotnet new winforms` would set
        // it up, which matters here since this project wasn't scaffolded
        // through that template.
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());

        GC.KeepAlive(mutex);
    }

    private static void SignalRunningInstance()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(ShowRequestEventName, out var showRequest)) return;
            using (showRequest)
            {
                AllowSetForegroundWindow(AsfwAny);
                showRequest.Set();
            }
        }
        catch
        {
            // The other instance may be mid-shutdown, or the event may not
            // exist on an older build still running from before this
            // version. Either way there's nothing useful to do but exit
            // quietly, exactly as this path did before.
        }
    }
}
