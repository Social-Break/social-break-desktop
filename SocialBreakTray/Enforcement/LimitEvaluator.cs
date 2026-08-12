using SocialBreakTray.Api;

namespace SocialBreakTray.Enforcement;

/// <summary>
/// Direct port of isLimitReached/getDomainLimit/getLogicalDjangoDay from the
/// browser extension's background.js - same plan-type semantics
/// (1=Complete Break, 2=Weekly Limit, 3=Daily Limit, 4=Custom Weekly Mix),
/// same "shift now by resetHour before computing which weekday a custom
/// rule applies to" logic, so a limit is enforced identically regardless of
/// which client (extension or desktop app) is doing the enforcing.
/// </summary>
public static class LimitEvaluator
{
    /// <summary>Mirrors getLogicalDjangoDay(resetHour) - Django's day-of-week
    /// convention is Monday=0..Sunday=6, unlike .NET's DayOfWeek
    /// (Sunday=0..Saturday=6), so this remaps after shifting the clock.</summary>
    public static int GetLogicalDjangoDay(int resetHour)
    {
        var shifted = DateTime.Now.AddHours(-resetHour);
        int dotNetDay = (int)shifted.DayOfWeek; // Sunday=0..Saturday=6
        return dotNetDay == 0 ? 6 : dotNetDay - 1; // -> Monday=0..Sunday=6
    }

    /// <summary>Per-app weekly limit, in seconds - 0 means unrestricted (no
    /// WeeklyAppRule for this identifier). Mirrors background.js's
    /// getDomainWeeklyLimit: PlanDto.WeeklyLimitMinutes (the old flat field)
    /// is never actually set by any reachable UI action server-side, so it
    /// always reads as 0 - real per-app weekly caps live in WeeklyRules,
    /// the same table the website/mobile editors write to.</summary>
    public static int GetWeeklyLimitSeconds(string identifier, PlanDto? plan)
    {
        if (plan == null) return 0;
        var rule = plan.WeeklyRules.FirstOrDefault(r => r.Domain == identifier);
        return rule?.LimitMinutes is { } limitMinutes ? limitMinutes * 60 : 0;
    }

    /// <summary>Returns the applicable daily limit in seconds for this
    /// identifier right now (0 means "no daily limit applies" for plan
    /// types where that's meaningful - callers must not treat 0 as itself a
    /// reached limit).</summary>
    public static int GetDailyLimitSeconds(string identifier, PlanDto? plan, int resetHour)
    {
        if (plan == null || plan.ActiveIdea == 0) return 0;
        if (plan.ActiveIdea == 1) return 0; // Complete Break has no separate daily concept - see IsLimitReached
        if (plan.ActiveIdea == 2) return GetWeeklyLimitSeconds(identifier, plan);

        // Daily Limit (3) and Custom Schedule (4) both check per-app/per-day
        // rules first, falling back to the flat daily limit when this app
        // has no rule for today - previously idea 3 skipped straight to the
        // flat limit, making the Edit Rules screen's per-app configuration
        // silently do nothing for Daily Limit users. Idea-3 rules never
        // have IsCompletelyBlocked=true (the server rejects that outside
        // Custom Schedule), so that branch is effectively idea-4-only in
        // practice, but harmless to check unconditionally here.
        if (plan.ActiveIdea == 3 || plan.ActiveIdea == 4)
        {
            int currentDjangoDay = GetLogicalDjangoDay(resetHour);
            var rule = plan.CustomRules.FirstOrDefault(r => r.Domain == identifier && r.DayOfWeek == currentDjangoDay);
            if (rule != null)
            {
                if (rule.IsCompletelyBlocked) return 0;
                if (rule.LimitMinutes is { } limitMinutes) return limitMinutes * 60;
            }
            return (plan.DailyLimitMinutes ?? 0) * 60;
        }

        return (plan.DailyLimitMinutes ?? 0) * 60;
    }

    public static bool IsLimitReached(string identifier, PlanDto? plan, int dailySecondsSoFar, int weeklySecondsSoFar, int resetHour)
    {
        if (plan == null || plan.ActiveIdea == 0) return false;
        if (plan.ActiveIdea == 1) return true; // Complete Break: always blocked, no accumulation needed

        int weeklyLimitSeconds = GetWeeklyLimitSeconds(identifier, plan);
        bool weeklyReached = weeklyLimitSeconds > 0 && weeklySecondsSoFar >= weeklyLimitSeconds;

        if (plan.ActiveIdea == 2) return weeklyReached;

        int dailyLimit = GetDailyLimitSeconds(identifier, plan, resetHour);
        bool dailyReached = dailyLimit > 0 && dailySecondsSoFar >= dailyLimit;

        if (plan.ActiveIdea == 3) return dailyReached;

        if (plan.ActiveIdea == 4) return dailyReached || weeklyReached;

        return false;
    }
}
