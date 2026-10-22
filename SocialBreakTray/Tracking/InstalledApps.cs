using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SocialBreakTray.Tracking;

public record InstalledApp(string Exe, string Name, string? Path);

/// <summary>
/// The programs installed on this computer, as the Start menu lists them -
/// the same names and icons people already know them by, so picking one to
/// limit never means knowing Discord is "discord.exe". Only shortcuts to a
/// program that's really there count: no uninstallers, no help files, no
/// pieces of Windows.
/// </summary>
public static class InstalledApps
{
    private static readonly string[] StartMenus =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
    };

    // Shortcuts that open something other than the program itself.
    private static readonly Regex NotAnApp = new(
        @"uninstall|unins\d|setup|install|update|updater|readme|help|documentation|manual|release notes|website|support|license|crash|report|repair|telemetry|language|settings|preferences|activation|adapter|driver|diagnostic|recording manager",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Launchers that start the real program under another name, and helpers
    // that only ever run inside something else.
    private static readonly HashSet<string> Helpers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome_proxy.exe", "msedge_proxy.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe",
        "cscript.exe", "msiexec.exe", "javaw.exe", "java.exe", "mmc.exe", "control.exe", "hh.exe",
        "appvlp.exe", "bgspawn.exe", "iexplore.exe", "mip.exe",
    };

    /// <summary>Everything the Start menu offers, A to Z, one entry per program.</summary>
    public static List<InstalledApp> Find()
    {
        var found = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return new List<InstalledApp>();
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            foreach (var menu in StartMenus.Where(Directory.Exists))
            {
                foreach (var lnk in SafeFiles(menu))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(lnk).Trim();
                    if (name.Length == 0 || NotAnApp.IsMatch(name)) continue;
                    var (target, arguments) = Resolve(shell!, lnk);
                    var app = FromShortcut(name, target, arguments, windows);
                    if (app != null && !found.ContainsKey(app.Exe)) found[app.Exe] = app;
                }
            }
        }
        catch
        {
            // Whatever was found before the fault is still worth showing.
        }
        finally
        {
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }

        return found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>What a shortcut opens, turned into an app - or null when it
    /// isn't one worth offering. Separate from the Start menu walk so it can
    /// be tested without one.</summary>
    public static InstalledApp? FromShortcut(string name, string? target, string? arguments, string windowsDir)
    {
        if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        if (!File.Exists(target)) return null;
        if (!string.IsNullOrEmpty(windowsDir) && target.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase)) return null;

        var exe = System.IO.Path.GetFileName(target).ToLowerInvariant();
        string? path = target;

        // Discord, Slack, Teams and others start through their own Update.exe
        // ("--processStart Discord.exe"); what runs, and what's counted, is
        // the program named after it.
        if (exe == "update.exe")
        {
            var started = Regex.Match(arguments ?? "", @"--processStart\s+""?([^""\s]+\.exe)", RegexOptions.IgnoreCase);
            if (!started.Success) return null;
            exe = started.Groups[1].Value.ToLowerInvariant();
            var beside = System.IO.Path.GetDirectoryName(target);
            path = beside == null ? null : LatestCopy(beside, exe);
        }

        if (Helpers.Contains(exe) || NotAnApp.IsMatch(System.IO.Path.GetFileNameWithoutExtension(exe))) return null;
        if (exe == "socialbreaktray.exe") return null;
        return new InstalledApp(exe, name, path);
    }

    /// <summary>The newest app-1.2.3 folder's copy of the program, for its icon.</summary>
    private static string? LatestCopy(string root, string exe)
    {
        try
        {
            return Directory.GetDirectories(root, "app-*")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => System.IO.Path.Combine(d, exe))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    private static (string? Target, string? Arguments) Resolve(object shell, string lnk)
    {
        object? shortcut = null;
        try
        {
            shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            if (shortcut == null) return (null, null);
            var type = shortcut.GetType();
            var target = type.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            var arguments = type.InvokeMember("Arguments", BindingFlags.GetProperty, null, shortcut, null) as string;
            return (target, arguments);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
        }
    }

    /// <summary>Every shortcut under a folder, skipping the ones Windows
    /// won't let us look into rather than giving up on the rest.</summary>
    private static IEnumerable<string> SafeFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            }).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
