using System.Text.Json.Serialization;

namespace SocialBreakTray.Api;

// Mirrors core/serializers.py's response shapes exactly - field names must
// match the JSON keys the Django API actually returns, not just read nicely.

public class LoginResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>What the server hands back when the app asks to be signed in
/// through the browser: the secret it polls with, and the page to open.</summary>
public class DeviceAuthStartResponse
{
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; set; }

    [JsonPropertyName("approval_url")]
    public string? ApprovalUrl { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>One answer to "has anybody approved me yet". Status is "pending"
/// until someone presses the button, then "approved" with a token exactly
/// once - or "denied", "expired", "invalid", all of which mean start over.</summary>
public class DeviceAuthPollResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

// MediaItemSerializer's fields = ['id', 'url', 'name', 'is_active', 'source_type'].
public class MediaItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "website";
}

// CustomAppRuleSerializer: 'domain' is literally sourced from media_item.url
// server-side (see core/serializers.py) - for a desktop_app row this holds
// the exe identifier (e.g. "code.exe"), not a real domain. Name kept as
// "Domain" here anyway to match the wire format exactly, not to imply it's
// always a website.
public class CustomRuleDto
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("day_of_week")]
    public int DayOfWeek { get; set; }

    [JsonPropertyName("is_completely_blocked")]
    public bool IsCompletelyBlocked { get; set; }

    [JsonPropertyName("limit_minutes")]
    public int? LimitMinutes { get; set; }

    // Time-of-day window, Custom Schedule (Premium) only - see
    // CustomAppRule.window_is_block's docstring in core/models.py. TimeSpan?
    // rather than string: System.Text.Json's built-in TimeSpan converter
    // (.NET 6+) round-trips exactly against DRF TimeField's "HH:MM:SS"
    // output with no manual parsing needed on this side.
    [JsonPropertyName("window_start")]
    public TimeSpan? WindowStart { get; set; }

    [JsonPropertyName("window_end")]
    public TimeSpan? WindowEnd { get; set; }

    [JsonPropertyName("window_is_block")]
    public bool WindowIsBlock { get; set; }

    // Every window the day has - up to three, all of WindowIsBlock's kind -
    // as "HH:MM" strings. The first is also in WindowStart/WindowEnd above;
    // empty from a server that predates more than one.
    [JsonPropertyName("windows")]
    public List<WindowDto> Windows { get; set; } = new();
}

public class WindowDto
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("end")]
    public string End { get; set; } = "";
}

// WeeklyAppRuleDomainSerializer: same domain-sourced-from-media_item.url
// pattern as CustomRuleDto above, but per-week rather than per-day.
public class WeeklyRuleDto
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("limit_minutes")]
    public int? LimitMinutes { get; set; }
}

// PlanSerializer's fields (baseline_daily_minutes is read-only server-side
// and irrelevant to limit enforcement, deliberately omitted here).
public class PlanDto
{
    [JsonPropertyName("active_idea")]
    public int ActiveIdea { get; set; }

    [JsonPropertyName("daily_limit_minutes")]
    public int? DailyLimitMinutes { get; set; }

    // Never actually set by any reachable UI action server-side (its only
    // writer has no caller anywhere) - kept on the DTO only because the wire
    // format still includes it, but LimitEvaluator must not read it for
    // enforcement. Real per-app weekly caps live in WeeklyRules below.
    [JsonPropertyName("weekly_limit_minutes")]
    public int? WeeklyLimitMinutes { get; set; }

    [JsonPropertyName("total_blockage_day")]
    public string? TotalBlockageDay { get; set; }

    [JsonPropertyName("custom_rules")]
    public List<CustomRuleDto> CustomRules { get; set; } = new();

    [JsonPropertyName("weekly_rules")]
    public List<WeeklyRuleDto> WeeklyRules { get; set; } = new();

    // Minutes earned today by ticking habits, by app identifier - added to
    // that app's limit today, as on the phone.
    [JsonPropertyName("bonus_today")]
    public Dictionary<string, int> BonusToday { get; set; } = new();

    // The day this plan was fetched: "today" in BonusToday means that day, so
    // a plan kept past midnight without a fresh sync earns nothing new.
    [JsonIgnore]
    public DateTime FetchedOn { get; set; } = DateTime.Today;

    // What each app has had today and this week on every device, by source,
    // so the computer counts the phone's and the browser's time against the
    // one shared limit. Null from a server that predates it.
    [JsonPropertyName("shared_usage")]
    public SharedUsageDto? SharedUsage { get; set; }
}

public class SharedUsageDto
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("apps")]
    public Dictionary<string, SharedAppDto> Apps { get; set; } = new();
}

public class SharedAppDto
{
    [JsonPropertyName("today")]
    public Dictionary<string, int> Today { get; set; } = new();

    [JsonPropertyName("week")]
    public Dictionary<string, int> Week { get; set; } = new();
}

public class ReportUsageResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("updated")]
    public int Updated { get; set; }
}
