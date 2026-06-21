using System.Text.Json;

namespace WinCodexBar.Providers;

/// <summary>
/// GitHub Copilot.
///
/// Token comes from config.Token or the COPILOT_API_TOKEN env var (a GitHub token with Copilot access).
/// [MEDIUM confidence] — copilot_internal is an UNOFFICIAL, undocumented API. // VERIFY endpoint + fields.
///   GET https://api.github.com/copilot_internal/user
///   Auth: header  Authorization: token <gh_token>
/// Returns copilot_plan + quota_snapshots (chat / completions / premium_interactions).
///
/// See CopilotDeviceFlow for an optional no-token-paste login.
/// </summary>
public sealed class CopilotProvider : IUsageProvider
{
    public string Id => "copilot";
    public string DisplayName => "GitHub Copilot";

    public bool IsLoggedIn(ProviderConfig cfg) =>
        !string.IsNullOrWhiteSpace(cfg.Token)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COPILOT_API_TOKEN"));

    public async Task<ProviderSnapshot> FetchAsync(ProviderConfig cfg, CancellationToken ct)
    {
        var snap = new ProviderSnapshot { ProviderId = Id, DisplayName = DisplayName };

        var token = cfg.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable("COPILOT_API_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            snap.Error = "No token. Put a GitHub token in config.json (Token) or set COPILOT_API_TOKEN, "
                       + "or run a device-flow login.";
            return snap;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/user");
            req.Headers.Add("Authorization", $"token {token}");
            req.Headers.Add("Accept", "application/json");

            using var res = await Net.Http.SendAsync(req, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new Exception("unauthorized — token invalid or lacks Copilot access");
            Net.EnsureOk(res);

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            if (root.TryGetProperty("copilot_plan", out var plan) && plan.ValueKind == JsonValueKind.String)
                snap.Plan = plan.GetString();

            DateTimeOffset? reset = null;
            if (root.TryGetProperty("quota_reset_date", out var qr) && qr.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(qr.GetString(), out var dt))
                reset = dt;

            if (root.TryGetProperty("quota_snapshots", out var snaps) && snaps.ValueKind == JsonValueKind.Object)
            {
                AddQuota(snap, snaps, "premium_interactions", "Premium requests", reset);
                AddQuota(snap, snaps, "chat", "Chat", reset);
                AddQuota(snap, snaps, "completions", "Completions", reset);
            }

            if (snap.Windows.Count == 0 && snap.Plan is not null)
                snap.Windows.Add(new UsageWindow { Label = "Plan", Detail = snap.Plan });

            if (snap.Windows.Count == 0)
                snap.Error = "no quota snapshots in response (API shape may have changed)";
        }
        catch (RateLimitException) { throw; }
        catch (Exception ex)
        {
            snap.Error = ex.Message;
        }

        return snap;
    }

    private static void AddQuota(ProviderSnapshot snap, JsonElement snaps, string key, string label, DateTimeOffset? reset)
    {
        if (!snaps.TryGetProperty(key, out var q) || q.ValueKind != JsonValueKind.Object)
            return;

        if (q.TryGetProperty("unlimited", out var unl) && unl.ValueKind == JsonValueKind.True)
        {
            snap.Windows.Add(new UsageWindow { Label = label, Utilization = null, Detail = "unlimited", ResetsAt = reset });
            return;
        }

        double? util = null;
        if (q.TryGetProperty("percent_remaining", out var pr) && pr.ValueKind == JsonValueKind.Number)
            util = Math.Clamp(1.0 - pr.GetDouble() / 100.0, 0, 1);

        string? detail = null;
        if (q.TryGetProperty("remaining", out var rem) && q.TryGetProperty("entitlement", out var ent))
            detail = $"{rem} / {ent}";

        snap.Windows.Add(new UsageWindow { Label = label, Utilization = util, Detail = detail, ResetsAt = reset });
    }
}
