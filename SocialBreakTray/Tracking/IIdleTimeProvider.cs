namespace SocialBreakTray.Tracking;

/// <summary>
/// Abstraction over "how long since the last keyboard/mouse input anywhere
/// on this machine" (OS-wide, not app-specific) - the desktop equivalent of
/// the browser extension's chrome.idle API ("the Walk Away Fix"). Kept
/// separate from its Win32 implementation for the same testability reason
/// as IForegroundWindowProvider.
/// </summary>
public interface IIdleTimeProvider
{
    TimeSpan GetIdleTime();
}
