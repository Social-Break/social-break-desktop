namespace SocialBreakTray.Tracking;

public record struct ForegroundInfo(string ProcessName, nint WindowHandle);

/// <summary>
/// Abstraction over "what's currently the foreground window's process,"
/// kept separate from its Win32 implementation specifically so
/// UsageAccumulator/LimitEvaluator's business logic can be unit-tested with
/// a fake, without needing a live Windows desktop session.
/// </summary>
public interface IForegroundWindowProvider
{
    /// <summary>Returns the foreground process's exe filename (lowercased,
    /// e.g. "code.exe") and its main window handle, or null if it can't be
    /// determined right now (no foreground window, access denied to an
    /// elevated/protected process, etc.) - callers should treat null as
    /// "skip this tick," never as fatal.</summary>
    ForegroundInfo? GetForegroundProcess();
}
