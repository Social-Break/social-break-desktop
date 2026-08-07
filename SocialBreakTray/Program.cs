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

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running - nothing to do. A future version could signal
            // the existing instance to flash its tray icon or similar;
            // silently exiting is the simplest correct behavior for now.
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
}
