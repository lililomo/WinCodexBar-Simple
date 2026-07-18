using System.Text.Json;
using WinCodexBar.Auth;

namespace WinCodexBar.Providers;

/// <summary>
/// Claude usage. Credential sources, in order:
///   1. claude.ai sessionKey cookie  -> GET claude.ai/api/organizations/{id}/usage   (most reliable)
///   2. OAuth access token           -> GET api.anthropic.com/api/oauth/usage         (CodexBar's method)
///      The token is taken from config (our login) or, failing that, from the Claude Code CLI's
///      ~/.claude/.credentials.json — so usage shows up with no separate login when Claude Code
///      is installed and its token is still valid.
///   3. Anthropic Admin key (sk-ant-admin...) -> org cost report.
///
/// Inner field names are parsed defensively (utilization, resets_at) — // VERIFY against a live payload.
/// </summary>
public sealed class ClaudeProvider : IUsageProvider
{
    public string Id => "claude";
    public string DisplayName => "Claude";

    public bool IsLoggedIn(ProviderConfig cfg) =>
        (cfg.OAuth?.IsValid ?? false)
        || !string.IsNullOrWhiteSpace(cfg.SessionKey)
        || !string.Equals(cfg.CookieSource, "Manual", StringComparison.OrdinalIgnoreCase)
        || ClaudeCliCreds.TryRead() is not null
        || (!string.IsNullOrWhiteSpace(cfg.ApiKey) && cfg.ApiKey!.StartsWith("sk-ant-admin", StringComparison.Ordinal));

    public async Task<ProviderSnapshot> FetchAsync(ProviderConfig cfg, CancellationToken ct)
    {
        var snap = new ProviderSnapshot { ProviderId = Id, DisplayName = DisplayName };

        // 0. Browser cookies (CodexBar-style) — read the claude.ai session straight from the browser.
        if (!string.Equals(cfg.CookieSource, "Manual", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var cookies = BrowserCookies.ClaudeCookies(cfg.CookieSource);
                if (cookies is null)
                {
                    snap.Error = $"Cookie claude.ai tidak ditemukan di {BrowserCookies.Display(cfg.CookieSource)}. Login claude.ai di browser itu dulu.";
                    return snap;
                }
                await FetchViaCookieHeader(cookies, snap, ct);
                if (snap.Windows.Count > 0) { snap.Plan ??= "claude.ai"; snap.Error = null; }
                else if (snap.Error is null) snap.Error = "usage kosong (bentuk API mungkin berubah)";
                return snap;
            }
            catch (RateLimitException) { throw; }
            catch (Exception ex)
            {
                snap.Error = $"cookie ({BrowserCookies.Display(cfg.CookieSource)}): {ex.Message}";
                return snap;
            }
        }

        // 1. sessionKey — returns the full usage windows reliably.
        if (!string.IsNullOrWhiteSpace(cfg.SessionKey))
        {
            try { await FetchViaWeb(cfg.SessionKey!.Trim(), snap, ct); return snap; }
            catch (RateLimitException) { throw; }
            catch (Exception ex) { snap.Error = $"web: {ex.Message}"; }
        }

        // 2. OAuth bearer token — ours, else the Claude Code CLI's stored token (CodexBar's path).
        string? token = cfg.OAuth?.IsValid == true ? cfg.OAuth!.AccessToken : null;
        if (token is null)
        {
            var cli = ClaudeCliCreds.TryRead();
            if (cli is not null)
            {
                if (cli.Plan is { Length: > 0 } plan) snap.Plan = plan;

                // Delegated refresh: if the Claude Code token has expired, let the official CLI
                // mint a fresh one (the refresh API itself is Cloudflare-locked).
                if (cli.Expired)
                {
                    try { await ClaudeCli.TryRefreshAsync(ct); } catch { /* handled below */ }
                    cli = ClaudeCliCreds.TryRead();
                }
                token = cli?.AccessToken;
            }
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                await FetchViaOAuth(token!, snap, ct);
                if (snap.Windows.Count > 0) { snap.Error = null; return snap; }
            }
            catch (UnauthorizedAccessException)
            {
                // Token rejected — try one delegated refresh, then retry once.
                try
                {
                    await ClaudeCli.TryRefreshAsync(ct);
                    var refreshed = ClaudeCliCreds.TryRead();
                    if (refreshed is not null)
                    {
                        snap.Windows.Clear();
                        await FetchViaOAuth(refreshed.AccessToken, snap, ct);
                        if (snap.Windows.Count > 0) { snap.Error = null; return snap; }
                    }
                }
                catch { /* fall through */ }
                snap.Error = "Token Claude Code kedaluwarsa & tak bisa di-refresh. Login ulang Claude Code, atau pakai \"tempel sessionKey\".";
            }
            catch (RateLimitException) { throw; }
            catch (Exception ex)
            {
                snap.Error = $"oauth: {ex.Message}";
            }
        }

        if (!string.IsNullOrWhiteSpace(cfg.ApiKey) && cfg.ApiKey!.StartsWith("sk-ant-admin", StringComparison.Ordinal))
        {
            try { await FetchViaAdmin(cfg.ApiKey!.Trim(), snap, ct); return snap; }
            catch (Exception ex) { snap.Error = (snap.Error is null ? "" : snap.Error + " | ") + $"admin: {ex.Message}"; }
        }

        snap.Error ??= "Belum login. Klik kanan ikon → Login → Claude (atau tempel sessionKey).";
        return snap;
    }

    // CodexBar's OAuth usage path.
    private static async Task FetchViaOAuth(string accessToken, ProviderSnapshot snap, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        req.Headers.Add("Authorization", $"Bearer {accessToken}");
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        req.Headers.Add("Accept", "application/json");

        using var res = await Net.Http.SendAsync(req, ct);
        if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("token kedaluwarsa/invalid");
        Net.EnsureOk(res); // surfaces 429 as RateLimitException

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        if (snap.Plan is null && root.TryGetProperty("subscriptionType", out var st) && st.ValueKind == JsonValueKind.String)
            snap.Plan = st.GetString();

        AddWindow(snap, root, "five_hour", "Session · 5h");
        AddWindow(snap, root, "seven_day", "Weekly");
        AddWindow(snap, root, "seven_day_sonnet", "Weekly · Sonnet");
        AddWindow(snap, root, "seven_day_opus", "Weekly · Opus");
        AddSpend(snap, root);
    }

    // "spend"/"extra_usage": pay-as-you-go credit consumption (amounts are in minor units).
    private static void AddSpend(ProviderSnapshot snap, JsonElement root)
    {
        if (!root.TryGetProperty("spend", out var s) || s.ValueKind != JsonValueKind.Object) return;
        if (s.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False) return;

        double? used = Money(s, "used");
        double? limit = Money(s, "limit");
        double? util = null;
        if (s.TryGetProperty("percent", out var pc) && pc.ValueKind == JsonValueKind.Number)
            util = Math.Clamp(pc.GetDouble() / 100.0, 0, 1);

        string? detail = used is not null
            ? (limit is not null ? $"${used:0.00} / ${limit:0.00}" : $"${used:0.00}")
            : null;
        if (util is null && detail is null) return;

        snap.Windows.Add(new UsageWindow { Label = "Extra usage", Utilization = util, Detail = detail });
    }

    private static double? Money(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var m) || m.ValueKind != JsonValueKind.Object) return null;
        if (!m.TryGetProperty("amount_minor", out var a) || a.ValueKind != JsonValueKind.Number) return null;
        int exp = m.TryGetProperty("exponent", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 2;
        return a.GetDouble() / Math.Pow(10, exp);
    }

    // Uses the browser's full claude.ai Cookie header (incl. cf_clearance) via curl.exe.
    private static async Task FetchViaCookieHeader(BrowserCookies.CookieSet ck, ProviderSnapshot snap, CancellationToken ct)
    {
        string? orgId = null;
        using (var orgDoc = await RequestDocCookie("https://claude.ai/api/organizations", ck, ct))
        {
            foreach (var org in orgDoc.RootElement.EnumerateArray())
            {
                if (!org.TryGetProperty("uuid", out var uuid)) continue;
                orgId ??= uuid.GetString();
                if (org.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                    foreach (var c in caps.EnumerateArray())
                        if (string.Equals(c.GetString(), "chat", StringComparison.OrdinalIgnoreCase)) { orgId = uuid.GetString(); break; }
            }
        }
        if (string.IsNullOrEmpty(orgId)) throw new Exception("no organization");

        using var usageDoc = await RequestDocCookie($"https://claude.ai/api/organizations/{orgId}/usage", ck, ct);
        var root = usageDoc.RootElement;
        AddWindow(snap, root, "five_hour", "Session · 5h");
        AddWindow(snap, root, "seven_day", "Weekly");
        AddWindow(snap, root, "seven_day_sonnet", "Weekly · Sonnet");
        AddWindow(snap, root, "seven_day_opus", "Weekly · Opus");
        AddSpend(snap, root);
    }

    private static async Task<JsonDocument> RequestDocCookie(string url, BrowserCookies.CookieSet ck, CancellationToken ct)
    {
        var (status, body) = await Curl.GetAsync(url, ck.Header, ck.UserAgent, ct);
        if (status == 429) throw new RateLimitException(null);
        if (status is 401 or 403) throw new Exception("cookie ditolak — login ulang claude.ai di browser");
        if (status is < 200 or >= 300) throw new Exception($"HTTP {status}");
        return JsonDocument.Parse(body);
    }

    private static async Task FetchViaWeb(string sessionKey, ProviderSnapshot snap, CancellationToken ct)
    {
        string? orgId;
        using (var orgDoc = await RequestDoc("https://claude.ai/api/organizations", sessionKey, ct))
        {
            orgId = null;
            foreach (var org in orgDoc.RootElement.EnumerateArray())
            {
                if (!org.TryGetProperty("uuid", out var uuid)) continue;
                orgId ??= uuid.GetString();

                // Prefer the org that has chat capability (the personal Claude org).
                if (org.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in caps.EnumerateArray())
                    {
                        if (string.Equals(c.GetString(), "chat", StringComparison.OrdinalIgnoreCase))
                        {
                            orgId = uuid.GetString();
                            break;
                        }
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(orgId))
            throw new Exception("no organization found");

        using var usageDoc = await RequestDoc($"https://claude.ai/api/organizations/{orgId}/usage", sessionKey, ct);
        var root = usageDoc.RootElement;
        AddWindow(snap, root, "five_hour", "Session · 5h");
        AddWindow(snap, root, "seven_day", "Weekly");
        AddWindow(snap, root, "seven_day_opus", "Weekly · Opus");
        if (snap.Windows.Count == 0)
            snap.Error = "usage payload had no known windows (API shape may have changed)";
    }

    private static async Task<JsonDocument> RequestDoc(string url, string sessionKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        req.Headers.Add("Accept", "application/json");

        using var res = await Net.Http.SendAsync(req, ct);
        if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new Exception("unauthorized — sessionKey kedaluwarsa, ambil ulang dari claude.ai");
        Net.EnsureOk(res); // surfaces 429 as RateLimitException

        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    private static void AddWindow(ProviderSnapshot snap, JsonElement root, string key, string label)
    {
        if (!root.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object)
            return;

        double? util = null;
        // Try a few plausible field names; normalise percentages (0..100) to fractions (0..1).
        foreach (var k in new[] { "utilization", "used_fraction", "percent_used", "used" })
        {
            if (w.TryGetProperty(k, out var u) && u.ValueKind == JsonValueKind.Number)
            {
                var v = u.GetDouble();
                util = v > 1.0 ? v / 100.0 : v;
                break;
            }
        }

        DateTimeOffset? reset = null;
        foreach (var k in new[] { "resets_at", "reset_at", "resetsAt", "resets" })
        {
            if (w.TryGetProperty(k, out var r) && r.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(r.GetString(), out var dt))
            {
                reset = dt;
                break;
            }
        }

        snap.Windows.Add(new UsageWindow { Label = label, Key = key, Utilization = util, ResetsAt = reset });
    }

    // VERIFY: the Anthropic Admin cost report endpoint/shape before relying on this.
    private static async Task FetchViaAdmin(string adminKey, ProviderSnapshot snap, CancellationToken ct)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.anthropic.com/v1/organizations/cost_report?starting_at={today}");
        req.Headers.Add("x-api-key", adminKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var res = await Net.Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));

        double spend = 0;
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var bucket in data.EnumerateArray())
            {
                if (bucket.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in results.EnumerateArray())
                    {
                        foreach (var name in new[] { "amount", "cost", "value" })
                        {
                            if (r.TryGetProperty(name, out var amt) && amt.ValueKind == JsonValueKind.Number)
                                spend += amt.GetDouble();
                        }
                    }
                }
            }
        }

        snap.Plan ??= "Admin API";
        snap.Windows.Add(new UsageWindow { Label = "Spend · today", Utilization = null, Detail = $"${spend:0.00}" });
    }
}
