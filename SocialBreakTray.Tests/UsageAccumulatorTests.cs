using SocialBreakTray.Tracking;
using Xunit;

namespace SocialBreakTray.Tests;

/// <summary>
/// Covers UsageAccumulator's rollover boundaries and its
/// persist-on-every-write durability - the two things its own summary calls
/// correctness requirements, and the two hardest things to check by hand
/// (verifying a weekly reset manually means waiting for a real Monday 3am).
///
/// Every test writes to its own throwaway temp file, never to the real
/// %APPDATA%\SocialBreak\usage.dat. That isolation matters: the live file
/// holds a real week of tracked totals, and POST /api/report-usage/ is a
/// snapshot overwrite server-side, so clobbering it locally would erase
/// already-recorded server-side time for that week.
///
/// Reference dates are real 2026 dates: Mon 2026-08-31, Wed 2026-09-02,
/// Sun 2026-09-06, Mon 2026-09-07. Note Sun 09-06 belongs to the week
/// beginning Mon 08-31, since weeks here start on Monday.
/// </summary>
public class UsageAccumulatorTests : IDisposable
{
    private const string App = "code.exe";
    private const string OtherApp = "discord.exe";
    private const int ResetHour = 3;

    private readonly string _dir;
    private readonly string _statePath;
    private DateTime _clock = new(2026, 9, 2, 12, 0, 0); // Wed noon

    public UsageAccumulatorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sbtray-tests-" + Guid.NewGuid().ToString("N"));
        _statePath = Path.Combine(_dir, "usage.dat");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private UsageAccumulator NewAccumulator() =>
        new(ResetHour, _statePath, () => _clock);

    // ---- Accumulation ----

    [Fact]
    public void AddSeconds_AccumulatesIntoBothDailyAndWeekly()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 5);
        acc.AddSeconds(App, 5);

        Assert.Equal(10, acc.DailySeconds[App]);
        Assert.Equal(10, acc.WeeklySeconds[App]);
    }

    [Fact]
    public void AddSeconds_TracksEachAppSeparately()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 30);
        acc.AddSeconds(OtherApp, 10);

        Assert.Equal(30, acc.DailySeconds[App]);
        Assert.Equal(10, acc.DailySeconds[OtherApp]);
    }

    [Fact]
    public void UntrackedApp_HasNoEntryRatherThanZero()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 5);
        Assert.False(acc.DailySeconds.ContainsKey(OtherApp));
    }

    // ---- Durability. This is the checklist's "kill it in Task Manager and
    // confirm the server-side weekly total did not drop" test, minus the
    // waiting: a second instance reading the same file stands in for a
    // relaunch after an uncontrolled exit. ----

    [Fact]
    public void Totals_SurviveAProcessRestart()
    {
        var before = NewAccumulator();
        before.AddSeconds(App, 600);

        var afterRelaunch = NewAccumulator();

        Assert.Equal(600, afterRelaunch.DailySeconds[App]);
        Assert.Equal(600, afterRelaunch.WeeklySeconds[App]);
    }

    [Fact]
    public void EveryWrite_IsFlushedImmediately_NotOnlyOnExit()
    {
        // Nothing disposes or closes the first instance - the file has to be
        // complete on disk the moment AddSeconds returns, because Windows
        // gives a killed process no chance to flush.
        var live = NewAccumulator();
        live.AddSeconds(App, 5);

        Assert.True(File.Exists(_statePath));
        Assert.Equal(5, NewAccumulator().DailySeconds[App]);
    }

    [Fact]
    public void CorruptStateFile_StartsFreshInsteadOfThrowing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_statePath, "this is not json");

        var acc = NewAccumulator();

        Assert.Empty(acc.DailySeconds);
        Assert.Empty(acc.WeeklySeconds);
    }

    [Fact]
    public void MissingStateFile_StartsEmpty()
    {
        var acc = NewAccumulator();
        Assert.Empty(acc.DailySeconds);
    }

    // ---- Daily rollover ----

    [Fact]
    public void NextDaySameWeek_ClearsDailyButKeepsWeekly()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 3, 12, 0, 0); // Thu noon
        acc.AddSeconds(App, 60);

        Assert.Equal(60, acc.DailySeconds[App]);
        Assert.Equal(660, acc.WeeklySeconds[App]);
    }

    [Fact]
    public void BeforeResetHour_IsStillTheSameLogicalDay()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        // 1am Thursday with a 3am reset is still Wednesday's bucket.
        _clock = new DateTime(2026, 9, 3, 1, 0, 0);
        acc.AddSeconds(App, 60);

        Assert.Equal(660, acc.DailySeconds[App]);
    }

    [Fact]
    public void CrossingResetHour_ClearsDaily()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 3, 3, 0, 0); // exactly 3am Thursday
        acc.AddSeconds(App, 60);

        Assert.Equal(60, acc.DailySeconds[App]);
        Assert.Equal(660, acc.WeeklySeconds[App]);
    }

    [Fact]
    public void RolloverAppliesOnReload_NotJustOnWrite()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 3, 12, 0, 0);
        var reloaded = NewAccumulator();

        Assert.Empty(reloaded.DailySeconds);
        Assert.Equal(600, reloaded.WeeklySeconds[App]);
    }

    // ---- Weekly rollover. Weeks start Monday, so Sunday still belongs to
    // the previous Monday's week. ----

    [Fact]
    public void SundayStillBelongsToTheWeekThatStartedMonday()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 6, 12, 0, 0); // Sun noon
        acc.AddSeconds(App, 60);

        Assert.Equal(60, acc.DailySeconds[App]);   // new day
        Assert.Equal(660, acc.WeeklySeconds[App]); // same week
    }

    [Fact]
    public void NewWeek_ClearsWeeklyToo()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 7, 12, 0, 0); // Mon noon, next week
        acc.AddSeconds(App, 60);

        Assert.Equal(60, acc.DailySeconds[App]);
        Assert.Equal(60, acc.WeeklySeconds[App]);
    }

    [Fact]
    public void MondayBeforeResetHour_IsStillLastWeek()
    {
        // The interaction worth pinning down: at 2am Monday with a 3am
        // reset, the logical day is still Sunday, so the weekly total must
        // not have reset yet.
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 7, 2, 0, 0);
        acc.AddSeconds(App, 60);

        Assert.Equal(660, acc.WeeklySeconds[App]);
    }

    [Fact]
    public void MondayAtResetHour_StartsTheNewWeek()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 7, 3, 0, 0);
        acc.AddSeconds(App, 60);

        Assert.Equal(60, acc.WeeklySeconds[App]);
    }

    [Fact]
    public void WeeklyRollover_ClearsEveryApp()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);
        acc.AddSeconds(OtherApp, 300);

        _clock = new DateTime(2026, 9, 7, 12, 0, 0);
        acc.AddSeconds(App, 60);

        Assert.Equal(60, acc.WeeklySeconds[App]);
        Assert.False(acc.WeeklySeconds.ContainsKey(OtherApp));
    }

    // ---- GetWeeklyMinutes: the shape POST /api/report-usage/ expects,
    // mirroring background.js's Math.round(seconds / 60). ----

    [Fact]
    public void WeeklyMinutes_RoundsToNearestMinute()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 89);       // 1.483 min -> 1
        acc.AddSeconds(OtherApp, 90);  // 1.5 min   -> 2

        var minutes = acc.GetWeeklyMinutes();

        Assert.Equal(1, minutes[App]);
        Assert.Equal(2, minutes[OtherApp]);
    }

    [Fact]
    public void WeeklyMinutes_ReportsWholeWeekNotJustToday()
    {
        // report-usage is a snapshot overwrite server-side, so this must
        // always be the full weekly figure - sending a daily number would
        // shrink the server's total.
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 3, 12, 0, 0);
        acc.AddSeconds(App, 600);

        Assert.Equal(20, acc.GetWeeklyMinutes()[App]);
        Assert.Equal(600, acc.DailySeconds[App]);
    }

    [Fact]
    public void WeeklyMinutes_AppliesPendingRolloverFirst()
    {
        var acc = NewAccumulator();
        acc.AddSeconds(App, 600);

        _clock = new DateTime(2026, 9, 7, 12, 0, 0); // next week
        Assert.Empty(acc.GetWeeklyMinutes());
    }

    [Fact]
    public void WeeklyMinutes_IsEmptyWithNoUsage()
    {
        Assert.Empty(NewAccumulator().GetWeeklyMinutes());
    }
}
