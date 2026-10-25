using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace SocialBreakTray.Tracking;

/// <summary>
/// How a program on the list looks to the person: "Discord" with Discord's own
/// icon, not "discord.exe" beside a letter. Found once per program - from a
/// copy that's running, or else the Start menu's shortcut to it - and kept for
/// the session. Slow the first time (the Start menu walk), so callers ask from
/// a background task and paint when it answers.
/// </summary>
internal static class AppLook
{
    private static readonly Dictionary<string, (string? Name, Bitmap? Icon)> Known = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static List<InstalledApp>? _installed;

    public static (string? Name, Bitmap? Icon) For(string exe, int iconSize)
    {
        lock (Gate)
        {
            if (Known.TryGetValue(exe, out var known)) return known;

            string? path = RunningPath(exe);
            string? name = null;
            if (path == null)
            {
                _installed ??= SafeInstalled();
                var app = _installed.FirstOrDefault(a => string.Equals(a.Exe, exe, StringComparison.OrdinalIgnoreCase));
                path = app?.Path;
                name = app?.Name;
            }
            name ??= Describe(path);

            var look = (name, IconOf(path, iconSize));
            Known[exe] = look;
            return look;
        }
    }

    /// <summary>The program's own icon at the given size, or null.</summary>
    public static Bitmap? IconOf(string? path, int size)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            using var full = icon.ToBitmap();
            var small = new Bitmap(size, size);
            using var g = Graphics.FromImage(small);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, 0, 0, size, size);
            return small;
        }
        catch
        {
            return null;
        }
    }

    private static string? RunningPath(string exe)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch
            {
                // Another user's, or elevated: no path for us.
            }
            finally
            {
                process.Dispose();
            }
        }
        return null;
    }

    /// <summary>The name a program gives itself in its file details.</summary>
    private static string? Describe(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var name = (info.FileDescription ?? "").Trim();
            if (name.Length is > 0 and <= 40) return name;
            name = (info.ProductName ?? "").Trim();
            return name.Length is > 0 and <= 40 ? name : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<InstalledApp> SafeInstalled()
    {
        try { return InstalledApps.Find(); }
        catch { return new List<InstalledApp>(); }
    }
}
