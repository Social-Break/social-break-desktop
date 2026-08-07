using SocialBreakTray.Api;

namespace SocialBreakTray.Tracking;

/// <summary>
/// Periodically POSTs the accumulator's current weekly totals to the same
/// /api/report-usage/ endpoint the browser extension already uses -
/// mirrors reportUsageToServer()'s "send a snapshot, not a delta" behavior
/// in background.js exactly, since the server-side endpoint expects that.
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
        var minutes = _accumulator.GetWeeklyMinutes();
        if (minutes.Count == 0) return;

        try
        {
            await _apiClient.ReportUsageAsync(minutes, ct);
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
