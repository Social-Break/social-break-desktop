using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SocialBreakTray.Api;

/// <summary>
/// Thin wrapper around the same REST API the browser extension already
/// talks to - same base URL, same endpoints, same token auth scheme
/// (Authorization: Token &lt;key&gt;). Deliberately calls /api/extension-login/
/// (not /api/api-token-auth/) for parity with the extension's actual
/// implementation, sending client_type: "desktop_app" so the backend's
/// ConnectedClient bookkeeping doesn't mistake this for the browser
/// extension - see get_extension_token in core/views.py.
/// </summary>
public class SocialBreakApiClient
{
    private const string BaseUrl = "https://social-break.com";

    private readonly HttpClient _http;
    private string? _token;

    public SocialBreakApiClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    public void SetToken(string token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", token);
    }

    public async Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var payload = new { username, password, client_type = "desktop_app" };
        using var response = await _http.PostAsJsonAsync("/api/extension-login/", payload, ct);

        // Distinguish "reached the server but it returned an error" (e.g. bad
        // credentials as 400, or a server-side 500) from genuine
        // unreachability (DNS/TLS/timeout, which instead throws
        // HttpRequestException/TaskCanceledException out of this method) -
        // a 500 in particular returns Django's generic HTML error page, not
        // JSON, so reading it as LoginResponse would otherwise surface as a
        // misleading "couldn't reach the server" in LoginForm's catch block.
        if (!response.IsSuccessStatusCode)
        {
            return new LoginResponse { Error = $"Server error ({(int)response.StatusCode}). Please try again later." };
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
    }

    public async Task<List<MediaItemDto>> GetMediaItemsAsync(CancellationToken ct = default)
    {
        var items = await _http.GetFromJsonAsync<List<MediaItemDto>>("/api/media/", ct);
        return items ?? new List<MediaItemDto>();
    }

    public async Task<PlanDto?> GetPlanAsync(CancellationToken ct = default)
    {
        // GET /api/plans/ returns a list (one Plan per user, via a
        // OneToOneField server-side) - see core/views.py's PlanViewSet.
        var plans = await _http.GetFromJsonAsync<List<PlanDto>>("/api/plans/", ct);
        return plans is { Count: > 0 } ? plans[0] : null;
    }

    /// <summary>
    /// Flat {identifier: minutes} snapshot of this week's cumulative totals -
    /// matches report_media_usage's exact expected payload shape in
    /// core/views.py. This is a snapshot overwrite server-side, not a delta,
    /// so the caller must always send the full current weekly total, never
    /// just "what changed since last time".
    /// </summary>
    public async Task<ReportUsageResponse?> ReportUsageAsync(Dictionary<string, int> minutesByIdentifier, CancellationToken ct = default)
    {
        if (minutesByIdentifier.Count == 0) return null;
        using var response = await _http.PostAsJsonAsync("/api/report-usage/", minutesByIdentifier, ct);
        return await response.Content.ReadFromJsonAsync<ReportUsageResponse>(cancellationToken: ct);
    }

    // Deliberately no "add a media item" method here - per the design,
    // managing the Media List (website or desktop app entries alike) only
    // ever happens on the website, never from this app. This client only
    // ever reads /api/media/ to know what to track, never writes to it.
}
