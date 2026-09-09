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

        // Distinguish "reached the server but it returned an error" from
        // genuine unreachability (DNS/TLS/timeout, which instead throws
        // HttpRequestException/TaskCanceledException out of this method).
        //
        // A rejected sign-in is a 4xx carrying a JSON explanation, and that
        // explanation is the whole point: it is where the server says a wrong
        // password was wrong, or that the account signs in with Google and
        // has no password to type here at all. This used to lump every
        // non-2xx together and show "Server error (401). Please try again
        // later." - which is neither true nor actionable for someone who
        // simply mistyped, and left a Google account with no way of finding
        // out why it could never get in.
        //
        // 5xx stays generic: Django answers those with an HTML error page,
        // not JSON, so there is no message to read.
        return await ReadLoginResultAsync(response, "That didn't match. Check your details and try again.", ct);
    }

    /// <summary>Shared by both ways in - a password and a connect code differ
    /// only in what gets posted; what comes back, and what a refusal means,
    /// are identical.</summary>
    private static async Task<LoginResponse?> ReadLoginResultAsync(
        HttpResponseMessage response, string genericFailure, CancellationToken ct)
    {
        if ((int)response.StatusCode >= 500)
        {
            return new LoginResponse { Error = $"Server error ({(int)response.StatusCode}). Please try again later." };
        }

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var failure = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
                if (!string.IsNullOrWhiteSpace(failure?.Error)) return failure;
            }
            catch
            {
                // Not JSON after all - fall through to the generic wording.
            }
            return new LoginResponse { Error = genericFailure };
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
    }

    /// <summary>
    /// Exchanges a one-time code from the website for the same token
    /// LoginAsync returns. This is the only route in for an account created
    /// through Google: it has no password, and a tray app has nowhere to put
    /// a "Continue with Google" button. The extension has redeemed codes at
    /// this endpoint since before the desktop app existed - the only new part
    /// is naming which client is asking, so the server records the right one.
    /// </summary>
    public async Task<LoginResponse?> ConnectWithCodeAsync(string code, CancellationToken ct = default)
    {
        var payload = new { code, client_type = "desktop_app" };
        using var response = await _http.PostAsJsonAsync("/api/extension-connect/", payload, ct);
        return await ReadLoginResultAsync(response, "That code didn't work. Get a fresh one from the website.", ct);
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
    /// {identifier: {"yyyy-MM-dd": minutes}} - one figure per app per logical
    /// day, which is what report_media_usage now accepts alongside the older
    /// flat weekly shape. Days let the server answer any date range; a weekly
    /// total could only ever answer "this week".
    ///
    /// Still a snapshot overwrite server-side, per day rather than per week, so
    /// re-sending a day the server already has replaces it instead of adding to
    /// it and a retry can't double-count.
    /// </summary>
    public async Task<ReportUsageResponse?> ReportUsageAsync(Dictionary<string, Dictionary<string, int>> minutesByDay, CancellationToken ct = default)
    {
        if (minutesByDay.Count == 0) return null;
        using var response = await _http.PostAsJsonAsync("/api/report-usage/", minutesByDay, ct);
        return await response.Content.ReadFromJsonAsync<ReportUsageResponse>(cancellationToken: ct);
    }

    // Deliberately no "add a media item" method here - per the design,
    // managing the Media List (website or desktop app entries alike) only
    // ever happens on the website, never from this app. This client only
    // ever reads /api/media/ to know what to track, never writes to it.
}
