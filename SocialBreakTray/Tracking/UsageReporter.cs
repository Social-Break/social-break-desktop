using SocialBreakTray.Api;

namespace SocialBreakTray.Tracking;

/// <summary>
/// Periodically POSTs the accumulator's per-day totals to the same
/// /api/report-usage/ endpoint the browser extension uses - mirrors
/// reportUsageToServer()'s "send a snapshot, not a delta" behaviour in
/// background.js, now per day rather than per week so the server can answer
/// any date range instead of only "this week".
/// </summary>
public class UsageReporter
{
    private readonly SocialBreakApiClient _apiClient;
    private readonly UsageAccumulator _accumulator;

    public UsageReporter(SocialBreakApiClient apiClient, UsageAccumulator accumulator)
    {
        _apiClient = apiClient;
        _accumulator = accumulator;
    }

    public async Task ReportAsync(CancellationToken ct = default)
    {
        var byDay = _accumulator.GetMinutesByDay();
        if (byDay.Count == 0) return;

        try
        {
            await _apiClient.ReportUsageAsync(byDay, ct);
            // Only now are finished days safe to forget. Clearing them before
            // the send succeeded would lose a day to a dropped connection -
            // exactly what the on-disk state exists to prevent.
            var reportedDays = byDay.Values.SelectMany(d => d.Keys).Distinct().ToList();
            _accumulator.ClearPendingDays(reportedDays);
        }
        catch
        {
            // Best-effort - a transient network failure just means this
            // report cycle's data waits for the next successful one. Since
            // the accumulator persists to disk independently of reporting,
            // nothing is lost locally even if every report attempt fails
            // for a while.
        }
    }
}
