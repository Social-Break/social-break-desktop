using Microsoft.Win32;

namespace SocialBreakTray;

/// <summary>
/// "Start with Windows" via the per-user HKCU Run key - deliberately does
/// NOT require administrator rights (HKCU is writable by the current user
/// with no elevation), and is exposed as a user-togglable tray menu item
/// rather than being silently enabled on first run, for the same
/// transparency reasons documented in legal.html's desktop-app disclosure -
/// a background app quietly registering itself to auto-launch without
/// asking is exactly the kind of thing that erodes trust.
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SocialBreakTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
