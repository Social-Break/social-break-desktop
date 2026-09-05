using SocialBreakTray.Api;
using SocialBreakTray.Enforcement;
using Xunit;

namespace SocialBreakTray.Tests;

/// <summary>
/// Covers LimitEvaluator, the hand-port of background.js's
/// isLimitReached/getDomainLimit/getLogicalDjangoDay. Plan types are
/// 1=Complete Break, 2=Weekly Limit, 3=Daily Limit, 4=Custom Weekly Mix.
///
/// Every case here fixes the clock explicitly rather than reading the real
/// one, so a test that passes today still passes on a Sunday, at 2am, or on
/// a machine in another timezone. All reference dates below are real 2026
/// dates: Mon 2026-08-31, Wed 2026-09-02, Sun 2026-09-06.
/// </summary>
public class LimitEvaluatorTests
{
    private const string App = "code.exe";

    private static readonly DateTime WedNoon = new(2026, 9, 2, 12, 0, 0);
    private static readonly DateTime SunNoon = new(2026, 9, 6, 12, 0, 0);
    private static readonly DateTime MonNoon = new(2026, 8, 31, 12, 0, 0);

    private const int Monday = 0, Tuesday = 1, Wednesday = 2, Sunday = 6;

    private static PlanDto Plan(
        int activeIdea,
        int? dailyLimitMinutes = null,
        List<CustomRuleDto>? customRules = null,
        List<WeeklyRuleDto>? weeklyRules = null) => new()
    {
        ActiveIdea = activeIdea,
        DailyLimitMinutes = dailyLimitMinutes,
        CustomRules = customRules ?? new List<CustomRuleDto>(),
        WeeklyRules = weeklyRules ?? new List<WeeklyRuleDto>(),
    };

    private static CustomRuleDto Rule(
        int dayOfWeek,
        string domain = App,
        bool completelyBlocked = false,
        int? limitMinutes = null,
        TimeSpan? windowStart = null,
        TimeSpan? windowEnd = null,
        bool windowIsBlock = false) => new()
    {
        Domain = domain,
        DayOfWeek = dayOfWeek,
        IsCompletelyBlocked = completelyBlocked,
        LimitMinutes = limitMinutes,
        WindowStart = windowStart,
        WindowEnd = windowEnd,
        WindowIsBlock = windowIsBlock,
    };

    private static WeeklyRuleDto WeeklyRule(int? limitMinutes, string domain = App) =>
        new() { Domain = domain, LimitMinutes = limitMinutes };

    // ---- GetLogicalDjangoDay: .NET Sunday=0..Saturday=6 remapped to
    // Django's Monday=0..Sunday=6, after shifting back by resetHour. ----

    [Fact]
    public void LogicalDay_MidAfternoon_IsThatCalendarWeekday()
    {
        Assert.Equal(Wednesday, LimitEvaluator.GetLogicalDjangoDay(3, WedNoon));
    }

    [Fact]
    public void LogicalDay_SundayRemapsToSix_NotZero()
    {
        // The remap most likely to be got wrong in a port: .NET calls Sunday
        // 0, Django calls it 6.
        Assert.Equal(Sunday, LimitEvaluator.GetLogicalDjangoDay(3, SunNoon));
    }

    [Fact]
    public void LogicalDay_BeforeResetHour_StillCountsAsThePreviousDay()
    {
        // 1am Wednesday with a 3am reset is still "Tuesday" as far as rules go.
        var wedEarlyMorning = new DateTime(2026, 9, 2, 1, 0, 0);
        Assert.Equal(Tuesday, LimitEvaluator.GetLogicalDjangoDay(3, wedEarlyMorning));
    }

    [Fact]
    public void LogicalDay_BeforeResetHourOnMonday_WrapsBackToSunday()
    {
        // Crosses both the reset boundary and the week boundary at once.
        var monEarlyMorning = new DateTime(2026, 8, 31, 1, 0, 0);
        Assert.Equal(Sunday, LimitEvaluator.GetLogicalDjangoDay(3, monEarlyMorning));
    }

    [Fact]
    public void LogicalDay_AtExactlyResetHour_IsTheNewDay()
    {
        var monAtReset = new DateTime(2026, 8, 31, 3, 0, 0);
        Assert.Equal(Monday, LimitEvaluator.GetLogicalDjangoDay(3, monAtReset));
    }

    // ---- GetWeeklyLimitSeconds: per-app WeeklyAppRule only. The old flat
    // Plan.weekly_limit_minutes field is deliberately never consulted. ----

    [Fact]
    public void WeeklyLimit_NoPlan_IsUnrestricted()
    {
        Assert.Equal(0, LimitEvaluator.GetWeeklyLimitSeconds(App, null));
    }

    [Fact]
    public void WeeklyLimit_NoRuleForThisApp_IsUnrestricted()
    {
        var plan = Plan(2, weeklyRules: new List<WeeklyRuleDto> { WeeklyRule(30, "discord.exe") });
        Assert.Equal(0, LimitEvaluator.GetWeeklyLimitSeconds(App, plan));
    }

    [Fact]
    public void WeeklyLimit_RuleForThisApp_IsConvertedToSeconds()
    {
        var plan = Plan(2, weeklyRules: new List<WeeklyRuleDto> { WeeklyRule(30) });
        Assert.Equal(30 * 60, LimitEvaluator.GetWeeklyLimitSeconds(App, plan));
    }

    [Fact]
    public void WeeklyLimit_RuleWithNullMinutes_IsUnrestricted()
    {
        var plan = Plan(2, weeklyRules: new List<WeeklyRuleDto> { WeeklyRule(null) });
        Assert.Equal(0, LimitEvaluator.GetWeeklyLimitSeconds(App, plan));
    }

    [Fact]
    public void WeeklyLimit_IgnoresTheDeadFlatPlanField()
    {
        // Regression guard for "Weekly Cap and Custom Mix now check per-app
        // WeeklyAppRule, not the dead flat field": setting only the flat
        // field must still read as unrestricted.
        var plan = Plan(2);
        plan.WeeklyLimitMinutes = 45;
        Assert.Equal(0, LimitEvaluator.GetWeeklyLimitSeconds(App, plan));
    }

    // ---- GetDailyLimitSeconds ----

    [Fact]
    public void DailyLimit_NoPlanOrNoActiveIdea_IsZero()
    {
        Assert.Equal(0, LimitEvaluator.GetDailyLimitSeconds(App, null, 3, WedNoon));
        Assert.Equal(0, LimitEvaluator.GetDailyLimitSeconds(App, Plan(0, 60), 3, WedNoon));
    }

    [Fact]
    public void DailyLimit_CompleteBreak_HasNoSeparateDailyNumber()
    {
        Assert.Equal(0, LimitEvaluator.GetDailyLimitSeconds(App, Plan(1, 60), 3, WedNoon));
    }

    [Fact]
    public void DailyLimit_WeeklyPlan_ReportsTheWeeklyCap()
    {
        var plan = Plan(2, weeklyRules: new List<WeeklyRuleDto> { WeeklyRule(90) });
        Assert.Equal(90 * 60, LimitEvaluator.GetDailyLimitSeconds(App, plan, 3, WedNoon));
    }

    [Fact]
    public void DailyLimit_DailyPlan_UsesTodaysPerAppRuleOverTheFlatLimit()
    {
        // Regression guard for "Daily Limit strategy now checks per-app
        // rules, not just the flat limit" - before that fix the Edit Rules
        // screen silently did nothing for idea-3 users.
        var plan = Plan(3, dailyLimitMinutes: 60,
            customRules: new List<CustomRuleDto> { Rule(Wednesday, limitMinutes: 15) });
        Assert.Equal(15 * 60, LimitEvaluator.GetDailyLimitSeconds(App, plan, 3, WedNoon));
    }

    [Fact]
    public void DailyLimit_DailyPlan_FallsBackToFlatLimitWhenNoRuleToday()
    {
        var plan = Plan(3, dailyLimitMinutes: 60,
            customRules: new List<CustomRuleDto> { Rule(Monday, limitMinutes: 15) });
        Assert.Equal(60 * 60, LimitEvaluator.GetDailyLimitSeconds(App, plan, 3, WedNoon));
    }

    [Fact]
    public void DailyLimit_RuleForAnotherAppIsIgnored()
    {
        var plan = Plan(3, dailyLimitMinutes: 60,
            customRules: new List<CustomRuleDto> { Rule(Wednesday, domain: "discord.exe", limitMinutes: 5) });
        Assert.Equal(60 * 60, LimitEvaluator.GetDailyLimitSeconds(App, plan, 3, WedNoon));
    }

    [Fact]
    public void DailyLimit_CompletelyBlockedRule_ReportsZeroNotTheFlatLimit()
    {
        var plan = Plan(4, dailyLimitMinutes: 60,
            customRules: new List<CustomRuleDto> { Rule(Wednesday, completelyBlocked: true) });
        Assert.Equal(0, LimitEvaluator.GetDailyLimitSeconds(App, plan, 3, WedNoon));
    }

    [Fact]
    public void DailyLimit_NoFlatLimitConfigured_IsZero()
    {
        Assert.Equal(0, LimitEvaluator.GetDailyLimitSeconds(App, Plan(3), 3, WedNoon));
    }

    // ---- IsBlockedToday ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BlockedToday_OnlyAppliesToDailyAndCustomPlans(int activeIdea)
    {
        var plan = Plan(activeIdea,
            customRules: new List<CustomRuleDto> { Rule(Wednesday, completelyBlocked: true) });
        Assert.False(LimitEvaluator.IsBlockedToday(App, plan, 3, WedNoon));
    }

    [Fact]
    public void BlockedToday_RuleForToday_Blocks()
    {
        var plan = Plan(4, customRules: new List<CustomRuleDto> { Rule(Wednesday, completelyBlocked: true) });
        Assert.True(LimitEvaluator.IsBlockedToday(App, plan, 3, WedNoon));
    }

    [Fact]
    public void BlockedToday_RuleForAnotherDay_DoesNotBlock()
    {
        // Regression guard for "Fix Custom Schedule's per-day Block
        // Completely: it never actually blocked" - the fix must not
        // over-correct into blocking every day of the week.
        var plan = Plan(4, customRules: new List<CustomRuleDto> { Rule(Monday, completelyBlocked: true) });
        Assert.False(LimitEvaluator.IsBlockedToday(App, plan, 3, WedNoon));
        Assert.True(LimitEvaluator.IsBlockedToday(App, plan, 3, MonNoon));
    }

    [Fact]
    public void BlockedToday_NoPlan_DoesNotBlock()
    {
        Assert.False(LimitEvaluator.IsBlockedToday(App, null, 3, WedNoon));
    }

    // ---- IsBlockedByWindowNow: WindowIsBlock flips the meaning of the
    // window. false (default) = "allowed only inside it"; true = "blocked
    // inside it". ----

    [Fact]
    public void Window_AllowWindow_InsideIsAllowed()
    {
        var plan = Plan(4, customRules: new List<CustomRuleDto>
        {
            Rule(Wednesday, windowStart: new TimeSpan(9, 0, 0), windowEnd: new TimeSpan(17, 0, 0)),
        });
        Assert.False(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, WedNoon));
    }

    [Fact]
    public void Window_AllowWindow_OutsideIsBlocked()
    {
        var plan = Plan(4, customRules: new List<CustomRuleDto>
        {
            Rule(Wednesday, windowStart: new TimeSpan(18, 0, 0), windowEnd: new TimeSpan(21, 0, 0)),
        });
        Assert.True(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, WedNoon));
    }

    [Fact]
    public void Window_BlockWindow_InsideIsBlocked()
    {
        var plan = Plan(4, customRules: new List<CustomRuleDto>
        {
            Rule(Wednesday, windowStart: new TimeSpan(9, 0, 0), windowEnd: new TimeSpan(17, 0, 0),
                windowIsBlock: true),
        });
        Assert.True(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, WedNoon));
    }

    [Fact]
    public void Window_BlockWindow_OutsideIsAllowed()
    {
        var plan = Plan(4, customRules: new List<CustomRuleDto>
        {
            Rule(Wednesday, windowStart: new TimeSpan(18, 0, 0), windowEnd: new TimeSpan(21, 0, 0),
                windowIsBlock: true),
        });
        Assert.False(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, WedNoon));
    }

    [Fact]
    public void Window_IsHalfOpen_EndBoundaryIsOutside()
    {
        // [start, end) - at exactly 17:00 an allow-window has closed.
        var plan = Plan(4, customRules: new List<CustomRuleDto>
        {
            Rule(Wednesday, windowStart: new TimeSpan(9, 0, 0), windowEnd: new TimeSpan(17, 0, 0)),
        });
        var atStart = new DateTime(2026, 9, 2, 9, 0, 0);
        var atEnd = new DateTime(2026, 9, 2, 17, 0, 0);
        Assert.False(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, atStart));
        Assert.True(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, atEnd));
    }

    [Fact]
    public void Window_BoundsUseRealWallClock_NotTheResetShiftedClock()
    {
        // Documented behavior: resetHour only picks which day's row to read.
        // A 09:00-17:00 window means literal 9-to-5 even with a 3am reset,
        // so 08:00 real time is outside it - it must not be shifted to 05:00
        // and compared that way.
        var plan = Plan(4, customRules: new List<CustomRuleDto>
        {
            Rule(Wednesday, windowStart: new TimeSpan(9, 0, 0), windowEnd: new TimeSpan(17, 0, 0)),
        });
        var wedEightAm = new DateTime(2026, 9, 2, 8, 0, 0);
        Assert.True(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, wedEightAm));
    }

    [Fact]
    public void Window_RuleWithoutBounds_NeverBlocks()
    {
        var plan = Plan(4, customRules: new List<CustomRuleDto> { Rule(Wednesday, limitMinutes: 30) });
        Assert.False(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, WedNoon));
    }

    [Fact]
    public void Window_OnlyOneBoundSet_NeverBlocks()
    {
        var plan = Plan(4, customRules: new List<CustomRuleDto>
        {
            Rule(Wednesday, windowStart: new TimeSpan(9, 0, 0)),
        });
        Assert.False(LimitEvaluator.IsBlockedByWindowNow(App, plan, 3, WedNoon));
    }

    // ---- IsLimitReached: the switchboard. Reason codes must stay in sync
    // with background.js's own strings. ----

    [Fact]
    public void Reached_NoPlanOrNoActiveIdea_IsNeverBlocked()
    {
        Assert.Null(LimitEvaluator.IsLimitReached(App, null, 99999, 99999, 3, WedNoon));
        Assert.Null(LimitEvaluator.IsLimitReached(App, Plan(0), 99999, 99999, 3, WedNoon));
    }

    [Fact]
    public void Reached_CompleteBreak_BlocksImmediatelyWithNoUsage()
    {
        Assert.Equal(LimitEvaluator.BlockReason.CompleteBreak,
            LimitEvaluator.IsLimitReached(App, Plan(1), 0, 0, 3, WedNoon));
    }

    [Fact]
    public void Reached_BlockedDay_TakesPrecedenceOverNumericLimits()
    {
        var plan = Plan(4, dailyLimitMinutes: 60,
            customRules: new List<CustomRuleDto> { Rule(Wednesday, completelyBlocked: true) });
        Assert.Equal(LimitEvaluator.BlockReason.BlockedDay,
            LimitEvaluator.IsLimitReached(App, plan, 0, 0, 3, WedNoon));
    }

    [Fact]
    public void Reached_TimeWindow_ReportedAsItsOwnReason()
    {
        var plan = Plan(4, dailyLimitMinutes: 60, customRules: new List<CustomRuleDto>
        {
            Rule(Wednesday, windowStart: new TimeSpan(18, 0, 0), windowEnd: new TimeSpan(21, 0, 0)),
        });
        Assert.Equal(LimitEvaluator.BlockReason.TimeWindow,
            LimitEvaluator.IsLimitReached(App, plan, 0, 0, 3, WedNoon));
    }

    [Fact]
    public void Reached_WeeklyPlan_BlocksOnlyOnTheWeeklyTotal()
    {
        var plan = Plan(2, weeklyRules: new List<WeeklyRuleDto> { WeeklyRule(30) });

        // Daily usage is irrelevant to a weekly plan.
        Assert.Null(LimitEvaluator.IsLimitReached(App, plan, 99999, 0, 3, WedNoon));
        Assert.Null(LimitEvaluator.IsLimitReached(App, plan, 0, 29 * 60, 3, WedNoon));
        Assert.Equal(LimitEvaluator.BlockReason.WeeklyLimit,
            LimitEvaluator.IsLimitReached(App, plan, 0, 30 * 60, 3, WedNoon));
    }

    [Fact]
    public void Reached_WeeklyPlanWithNoCapForThisApp_NeverBlocks()
    {
        // The "0 means unrestricted, not already-reached" distinction.
        Assert.Null(LimitEvaluator.IsLimitReached(App, Plan(2), 0, 99999, 3, WedNoon));
    }

    [Fact]
    public void Reached_DailyPlan_BlocksOnlyOnTodaysTotal()
    {
        var plan = Plan(3, dailyLimitMinutes: 30);

        Assert.Null(LimitEvaluator.IsLimitReached(App, plan, 0, 99999, 3, WedNoon));
        Assert.Null(LimitEvaluator.IsLimitReached(App, plan, 29 * 60, 0, 3, WedNoon));
        Assert.Equal(LimitEvaluator.BlockReason.DailyLimit,
            LimitEvaluator.IsLimitReached(App, plan, 30 * 60, 0, 3, WedNoon));
    }

    [Fact]
    public void Reached_DailyPlanWithNoLimitConfigured_NeverBlocks()
    {
        Assert.Null(LimitEvaluator.IsLimitReached(App, Plan(3), 99999, 99999, 3, WedNoon));
    }

    [Fact]
    public void Reached_ExactlyAtTheLimit_CountsAsReached()
    {
        var plan = Plan(3, dailyLimitMinutes: 30);
        Assert.Equal(LimitEvaluator.BlockReason.DailyLimit,
            LimitEvaluator.IsLimitReached(App, plan, 1800, 0, 3, WedNoon));
        Assert.Null(LimitEvaluator.IsLimitReached(App, plan, 1799, 0, 3, WedNoon));
    }

    [Fact]
    public void Reached_CustomMix_ChecksBothCeilings()
    {
        var plan = Plan(4, dailyLimitMinutes: 30,
            weeklyRules: new List<WeeklyRuleDto> { WeeklyRule(120) });

        Assert.Null(LimitEvaluator.IsLimitReached(App, plan, 0, 0, 3, WedNoon));
        Assert.Equal(LimitEvaluator.BlockReason.DailyLimit,
            LimitEvaluator.IsLimitReached(App, plan, 30 * 60, 0, 3, WedNoon));
        Assert.Equal(LimitEvaluator.BlockReason.WeeklyLimit,
            LimitEvaluator.IsLimitReached(App, plan, 0, 120 * 60, 3, WedNoon));
    }

    [Fact]
    public void Reached_CustomMix_PrefersDailyWhenBothAreReached()
    {
        // Arbitrary but deterministic, and must match background.js's order.
        var plan = Plan(4, dailyLimitMinutes: 30,
            weeklyRules: new List<WeeklyRuleDto> { WeeklyRule(120) });
        Assert.Equal(LimitEvaluator.BlockReason.DailyLimit,
            LimitEvaluator.IsLimitReached(App, plan, 30 * 60, 120 * 60, 3, WedNoon));
    }

    [Fact]
    public void Reached_ReasonCodesMatchTheExtensionsStrings()
    {
        // block.html reads these off the query string, so they are a wire
        // contract shared with the browser extension, not internal names.
        Assert.Equal("complete_break", LimitEvaluator.BlockReason.CompleteBreak);
        Assert.Equal("blocked_day", LimitEvaluator.BlockReason.BlockedDay);
        Assert.Equal("time_window", LimitEvaluator.BlockReason.TimeWindow);
        Assert.Equal("daily_limit", LimitEvaluator.BlockReason.DailyLimit);
        Assert.Equal("weekly_limit", LimitEvaluator.BlockReason.WeeklyLimit);
    }
}
