using SocialBreakTray.Api;
using SocialBreakTray.Enforcement;
using Xunit;

namespace SocialBreakTray.Tests;

/// <summary>
/// LimitEvaluator.SimpleRule: whether an app's rule is one the desktop app's
/// limit picker can show and change (the same limit or block every day), or
/// a Premium schedule it must leave to the website rather than flatten.
/// </summary>
public class SimpleRuleTests
{
    private const string App = "vivaldi.exe";

    private static PlanDto Plan(IEnumerable<CustomRuleDto>? days = null, IEnumerable<WeeklyRuleDto>? weekly = null) => new()
    {
        ActiveIdea = 4,
        CustomRules = days?.ToList() ?? new List<CustomRuleDto>(),
        WeeklyRules = weekly?.ToList() ?? new List<WeeklyRuleDto>(),
    };

    private static IEnumerable<CustomRuleDto> EveryDay(int? minutes, bool blocked = false) =>
        Enumerable.Range(0, 7).Select(d => new CustomRuleDto { Domain = App, DayOfWeek = d, LimitMinutes = minutes, IsCompletelyBlocked = blocked });

    [Fact]
    public void No_rule_yet_is_simple_with_nothing_chosen()
    {
        Assert.Equal((false, (int?)null, false), LimitEvaluator.SimpleRule(Plan(), App));
    }

    [Fact]
    public void The_same_limit_every_day_is_shown()
    {
        Assert.Equal((false, (int?)30, false), LimitEvaluator.SimpleRule(Plan(EveryDay(30)), App));
    }

    [Fact]
    public void Blocked_every_day_is_shown()
    {
        Assert.Equal((false, (int?)null, true), LimitEvaluator.SimpleRule(Plan(EveryDay(null, blocked: true)), App));
    }

    [Fact]
    public void Other_apps_rules_dont_count()
    {
        var rules = EveryDay(30).Select(r => { r.Domain = "discord.exe"; return r; });
        Assert.Equal((false, (int?)null, false), LimitEvaluator.SimpleRule(Plan(rules), App));
    }

    [Fact]
    public void Weekends_different_is_a_schedule()
    {
        var rules = EveryDay(30).Select(r => { if (r.DayOfWeek >= 5) r.LimitMinutes = 60; return r; });
        Assert.True(LimitEvaluator.SimpleRule(Plan(rules), App).Schedule);
    }

    [Fact]
    public void A_time_window_is_a_schedule()
    {
        var rules = EveryDay(30).Select(r => { r.WindowStart = TimeSpan.FromHours(18); r.WindowEnd = TimeSpan.FromHours(22); return r; });
        Assert.True(LimitEvaluator.SimpleRule(Plan(rules), App).Schedule);
    }

    [Fact]
    public void A_weekly_limit_is_a_schedule()
    {
        Assert.True(LimitEvaluator.SimpleRule(Plan(weekly: new[] { new WeeklyRuleDto { Domain = App, LimitMinutes = 300 } }), App).Schedule);
    }
}
