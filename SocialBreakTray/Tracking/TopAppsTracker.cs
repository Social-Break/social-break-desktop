using System.Diagnostics;
using System.Text.Json;

namespace SocialBreakTray.Tracking;

public record TopApp(string Exe, string Name, int Minutes);

/// <summary>
/// The programs used most on this computer this week, so the website can
/// offer them by name to add to the list ("Used most on your computer") -
/// nobody should need to know Discord is "discord.exe". Counts every program
/// in front, a second at a time, except Windows' own parts and this app;
/// sends only the top ten's file name, name and minutes, and only while the
/// tray's "Suggest my most-used apps" is on. Starts over each Monday.
/// </summary>
public class TopAppsTracker
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SocialBreak", "topapps.json");

    // Windows' own pieces, which are "in front" without being anything anyone
    // would want to limit.
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "searchhost.exe", "searchapp.exe", "shellexperiencehost.exe",
        "startmenuexperiencehost.exe", "lockapp.exe", "applicationframehost.exe", "textinputhost.exe",
        "socialbreaktray.exe", "taskmgr.exe", "dwm.exe", "systemsettings.exe", "shellhost.exe",
        "widgets.exe", "securityhealthsystray.exe", "rundll32.exe", "consent.exe",
    };

    private class State
    {
        public string WeekStart { get; set; } = "";
        public Dictionary<string, int> Seconds { get; set; } = new();
        public Dictionary<string, string> Names { get; set; } = new();
    }

    private readonly string _path;
    private readonly Func<DateTime> _now;
    private State _state;
    private string _lastSent = "";

    /// <summary><paramref name="path"/> and <paramref name="now"/> are test seams.</summary>
    public TopAppsTracker(string? path = null, Func<DateTime>? now = null)
    {
        _path = path ?? DefaultPath;
        _now = now ?? (() => DateTime.Now);
        _state = Load(_path);
    }

    private string MondayOf(DateTime when) => when.Date.AddDays(-(((int)when.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd");

    private void Rollover()
    {
        var monday = MondayOf(_now());
        if (_state.WeekStart == monday) return;
        _state = new State { WeekStart = monday };
    }

    /// <summary>One more second of this program in front.</summary>
    public void Add(string exe, string? fullPath, int seconds = 1)
    {
        if (string.IsNullOrEmpty(exe) || Ignored.Contains(exe)) return;
        Rollover();
        _state.Seconds[exe] = _state.Seconds.GetValueOrDefault(exe) + seconds;
        if (!_state.Names.ContainsKey(exe)) _state.Names[exe] = NameOf(exe, fullPath);
    }

    /// <summary>The name a program shows itself as ("Discord"), from its file's
    /// description - or its file name, tidied, when it gives none.</summary>
    private static string NameOf(string exe, string? fullPath)
    {
        try
        {
            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                var info = FileVersionInfo.GetVersionInfo(fullPath);
                var name = (info.FileDescription ?? "").Trim();
                if (name.Length is > 0 and <= 60) return name;
                name = (info.ProductName ?? "").Trim();
                if (name.Length is > 0 and <= 60) return name;
            }
        }
        catch
        {
            // Unreadable: the file name will do.
        }
        var stem = Path.GetFileNameWithoutExtension(exe);
        return stem.Length == 0 ? exe : char.ToUpperInvariant(stem[0]) + stem[1..];
    }

    /// <summary>This week's most used, most first - only those used a minute or more.</summary>
    public List<TopApp> Top(int count = 10)
    {
        Rollover();
        return _state.Seconds
            .Where(kv => kv.Value >= 60)
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .Select(kv => new TopApp(kv.Key, _state.Names.GetValueOrDefault(kv.Key) ?? kv.Key, kv.Value / 60))
            .ToList();
    }

    /// <summary>The top ten if they've changed since last sent, else null.</summary>
    public List<TopApp>? ChangedTop()
    {
        var top = Top();
        var key = string.Join("|", top.Select(a => $"{a.Exe}:{a.Minutes}"));
        if (key == _lastSent) return null;
        _lastSent = key;
        return top;
    }

    /// <summary>Forgotten: the switch turned off.</summary>
    public void Clear()
    {
        _state = new State { WeekStart = MondayOf(_now()) };
        _lastSent = "";
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_state));
        }
        catch
        {
            // Only a convenience: lost, the week's count starts again.
        }
    }

    private static State Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State() : new State();
        }
        catch
        {
            return new State();
        }
    }
}
