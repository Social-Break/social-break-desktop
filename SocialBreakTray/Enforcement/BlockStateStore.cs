using System.Text.Json;

namespace SocialBreakTray.Enforcement;

internal class BlockState
{
    public HashSet<string> AlreadyBlockedToday { get; set; } = new();
    public HashSet<string> DismissedForToday { get; set; } = new();
    public Dictionary<string, DateTime> SnoozeExemptions { get; set; } = new();
    public string? LogicalDay { get; set; } // ISO date string, e.g. "2026-08-29"
}

/// <summary>
/// Persists the three "for today" enforcement states - which apps have
/// already shown the block overlay today (switches BlockForm to its
/// "Continue nonetheless" repeat framing), which ones the user has
/// permanently dismissed for today, and active 5-minute snooze exemptions -
/// so a process restart doesn't quietly reset them.
///
/// Without this, a crash, a Windows Update reboot, or just closing and
/// reopening the app mid-day would silently forget "you've already seen
/// this once today," showing the first-time "Snooze 5 minutes" framing
/// again right after the user had already progressed to "Continue
/// nonetheless." Not a data-loss bug the way UsageAccumulator's persistence
/// is (nothing server-side gets overwritten by this), but a real,
/// noticeable UX regression - a restart shouldn't make the app "forget" a
/// choice the user already made today.
///
/// Same day-rollover pattern as UsageAccumulator (shifted by resetHour,
/// mirroring getLogicalDjangoDay()), stored as plain JSON rather than
/// DPAPI-encrypted like TokenStore - none of this is sensitive, it's just
/// "did we already nag about app X today."
/// </summary>
public class BlockStateStore
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SocialBreak", "blockstate.dat");

    private readonly int _resetHour;
    private BlockState _state;

    public BlockStateStore(int resetHour)
    {
        _resetHour = resetHour;
        _state = Load();
        ApplyRolloverIfNeeded();
    }

    public bool HasAlreadyBlockedToday(string identifier)
    {
        ApplyRolloverIfNeeded();
        return _state.AlreadyBlockedToday.Contains(identifier);
    }

    public void MarkBlockedToday(string identifier)
    {
        ApplyRolloverIfNeeded();
        _state.AlreadyBlockedToday.Add(identifier);
        Save();
    }

    public bool IsDismissedForToday(string identifier)
    {
        ApplyRolloverIfNeeded();
        return _state.DismissedForToday.Contains(identifier);
    }

    public void MarkDismissedForToday(string identifier)
    {
        ApplyRolloverIfNeeded();
        _state.DismissedForToday.Add(identifier);
        Save();
    }

    public bool IsSnoozed(string identifier)
    {
        ApplyRolloverIfNeeded();
        return _state.SnoozeExemptions.TryGetValue(identifier, out var until) && DateTime.UtcNow < until;
    }

    public void Snooze(string identifier, TimeSpan duration)
    {
        ApplyRolloverIfNeeded();
        _state.SnoozeExemptions[identifier] = DateTime.UtcNow.Add(duration);
        Save();
    }

    private DateOnly LogicalToday()
    {
        var shifted = DateTime.Now.AddHours(-_resetHour);
        return DateOnly.FromDateTime(shifted);
    }

    private void ApplyRolloverIfNeeded()
    {
        var todayStr = LogicalToday().ToString("yyyy-MM-dd");
        if (_state.LogicalDay == todayStr) return;

        _state.AlreadyBlockedToday.Clear();
        _state.DismissedForToday.Clear();
        _state.SnoozeExemptions.Clear();
        _state.LogicalDay = todayStr;
        Save();
    }

    private static BlockState Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return new BlockState();
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<BlockState>(json) ?? new BlockState();
        }
        catch
        {
            // Corrupt/unreadable file - start fresh rather than crash. Worst
            // case this shows the first-time framing once more than it
            // strictly should, which is harmless.
            return new BlockState();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StatePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_state));
        }
        catch
        {
            // Best-effort, matching UsageAccumulator/TokenStore - a failed
            // write here just risks nagging once more than intended next
            // time, not a correctness issue.
        }
    }
}
