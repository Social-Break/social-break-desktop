using System.Text.Json;

namespace SocialBreakTray.Tracking;

internal class AccumulatorState
{
    public Dictionary<string, int> DailySeconds { get; set; } = new();
    public Dictionary<string, int> WeeklySeconds { get; set; } = new();
    public string? LogicalDay { get; set; }       // ISO date string, e.g. "2026-08-07"
    public string? LogicalWeekMonday { get; set; } // ISO date string of that week's Monday
}

/// <summary>
/// Per-app running totals (seconds today, seconds this week), persisted to
/// disk on every update and reloaded at startup instead of zero-initializing.
///
/// This durability is a correctness requirement, not a nice-to-have:
/// POST /api/report-usage/ is a snapshot overwrite server-side (see
/// report_media_usage in core/views.py - it does an update_or_create with
/// the raw total, not a delta). An in-memory-only accumulator would reset to
/// 0 on any uncontrolled process exit (crash, forced reboot, a Task-Manager
/// kill, log-off - Windows only gives a "not responding" process a few
/// seconds before force-killing it on shutdown, so a graceful flush-on-exit
/// can't be relied on either), and the next report after that would
/// silently overwrite the server's real weekly total with a smaller number,
/// erasing already-recorded time for that week. Persisting to disk on every
/// tick is what chrome.storage.local already gives the browser extension
/// for free; this class exists to give the desktop app the same guarantee.
///
/// "Logical day"/"logical week" both shift by the same configurable
/// resetHour before computing calendar boundaries, mirroring
/// getLogicalDjangoDay() in the browser extension's background.js (which
/// only applied this shift to the daily-limit weekday lookup) - applied
/// consistently to both the daily AND weekly rollover here, which is a
/// slightly cleaner generalization of the same idea, not a behavior change
/// in practice (the extension's weekly reset already only ever fires once,
/// exactly at resetHour, via its alarm).
/// </summary>
public class UsageAccumulator
{
    private static readonly string DefaultStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SocialBreak", "usage.dat");

    private readonly int _resetHour;
    private readonly string _statePath;
    private readonly Func<DateTime> _now;
    private AccumulatorState _state;

    /// <summary><paramref name="statePath"/> and <paramref name="now"/> are
    /// test seams only - production constructs this with neither, which keeps
    /// the real %APPDATA%\SocialBreak\usage.dat path and the real clock
    /// exactly as before. They exist because the rollover boundaries below
    /// are otherwise only observable by waiting for a real 3am (or a real
    /// Monday), and because a test must never be able to overwrite the live
    /// usage file - doing so would destroy a real week's tracked totals, for
    /// the reasons spelled out in this class's summary.</summary>
    public UsageAccumulator(int resetHour = 3, string? statePath = null, Func<DateTime>? now = null)
    {
        _resetHour = resetHour;
        _statePath = statePath ?? DefaultStatePath;
        _now = now ?? (() => DateTime.Now);
        _state = Load(_statePath);
        ApplyRolloverIfNeeded();
    }

    public IReadOnlyDictionary<string, int> DailySeconds => _state.DailySeconds;
    public IReadOnlyDictionary<string, int> WeeklySeconds => _state.WeeklySeconds;

    public void AddSeconds(string identifier, int seconds)
    {
        ApplyRolloverIfNeeded();
        _state.DailySeconds[identifier] = _state.DailySeconds.GetValueOrDefault(identifier) + seconds;
        _state.WeeklySeconds[identifier] = _state.WeeklySeconds.GetValueOrDefault(identifier) + seconds;
        Save();
    }

    /// <summary>Weekly totals converted to whole minutes, for
    /// POST /api/report-usage/ - mirrors reportUsageToServer()'s
    /// Math.round(seconds / 60) in background.js.</summary>
    public Dictionary<string, int> GetWeeklyMinutes()
    {
        ApplyRolloverIfNeeded();
        return _state.WeeklySeconds.ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value / 60.0));
    }

    private DateOnly LogicalToday()
    {
        var shifted = _now().AddHours(-_resetHour);
        return DateOnly.FromDateTime(shifted);
    }

    private static DateOnly MondayOf(DateOnly day)
    {
        // DayOfWeek: Sunday=0..Saturday=6. Convert to a Monday-first offset.
        int offset = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-offset);
    }

    private void ApplyRolloverIfNeeded()
    {
        var today = LogicalToday();
        var monday = MondayOf(today);
        var todayStr = today.ToString("yyyy-MM-dd");
        var mondayStr = monday.ToString("yyyy-MM-dd");

        bool changed = false;
        if (_state.LogicalDay != todayStr)
        {
            _state.DailySeconds.Clear();
            _state.LogicalDay = todayStr;
            changed = true;
        }
        if (_state.LogicalWeekMonday != mondayStr)
        {
            _state.WeeklySeconds.Clear();
            _state.LogicalWeekMonday = mondayStr;
            changed = true;
        }
        if (changed) Save();
    }

    private static AccumulatorState Load(string statePath)
    {
        try
        {
            if (!File.Exists(statePath)) return new AccumulatorState();
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<AccumulatorState>(json) ?? new AccumulatorState();
        }
        catch
        {
            // Corrupt/unreadable file - start fresh rather than crash. Worst
            // case this loses today's/this week's local progress, it does
            // not corrupt server data (report-usage overwrites with
            // whatever we send next, so a fresh-start local total just
            // means the next report is smaller than the true figure until
            // it catches back up).
            return new AccumulatorState();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(_state));
        }
        catch
        {
            // Best-effort - a failed disk write shouldn't crash tracking,
            // though it does mean this tick's progress risks being lost if
            // the process dies before a later successful save.
        }
    }
}
