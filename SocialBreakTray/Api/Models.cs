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
}

public class ReportUsageResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("updated")]
    public int Updated { get; set; }
}
