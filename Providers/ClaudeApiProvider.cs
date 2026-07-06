using System.Text.Json;

namespace WinCodexBar.Providers;

/// <summary>
/// Claude via the Anthropic API — paste an API key. Subscription usage windows aren't exposed to
/// API keys; an Admin key (sk-ant-admin...) returns org spend via the cost report, a normal key
/// (sk-ant-api...) just shows "connected". Configure via config.json or the tray Login menu.
/// </summary>
public sealed class ClaudeApiProvider : IUsageProvider
{
    public string Id => "claude-api";
    public string DisplayName => "Claude API";

    public bool IsLoggedIn(ProviderConfig cfg) =>
        !string.IsNullOrWhiteSpace(cfg.ApiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    public async Task<ProviderSnapshot> FetchAsync(ProviderConfig cfg, CancellationToken ct)
    {
        var snap = new ProviderSnapshot { ProviderId = Id, DisplayName = DisplayName };

        var key = cfg.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            snap.Error = "Belum ada API key. Klik kanan ikon → Login → Claude API.";
            return snap;
        }

        key = key.Trim();

        // 1. Validate the key — any valid key can list models (Admin-ness not required).
        using (var vreq = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=1"))
        {
            vreq.Headers.Add("x-api-key", key);
            vreq.Headers.Add("anthropic-version", "2023-06-01");
            using var vres = await Net.Http.SendAsync(vreq, ct);
            if (vres.StatusCode is System.Net.HttpStatusCode.Unauthorized)
                throw new Exception("API key invalid");
            if ((int)vres.StatusCode == 429)
                throw new RateLimitException(Net.RetryAfter(vres));
            vres.EnsureSuccessStatusCode();
        }

        // 2. Spend needs an Admin key (sk-ant-admin…). Try it; a normal key just shows "connected".
        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            using var creq = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.anthropic.com/v1/organizations/cost_report?starting_at={today}");
            creq.Headers.Add("x-api-key", key);
            creq.Headers.Add("anthropic-version", "2023-06-01");
            using var cres = await Net.Http.SendAsync(creq, ct);
            if ((int)cres.StatusCode == 429) throw new RateLimitException(Net.RetryAfter(cres));
            if (cres.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await cres.Content.ReadAsStringAsync(ct));
                snap.Plan = "Admin API";
                snap.Windows.Add(new UsageWindow { Label = "Spend · today", Detail = $"${SumCost(doc.RootElement):0.00}" });
                return snap;
            }
        }
        catch (RateLimitException) { throw; }
        catch { /* not an Admin key — fall through */ }

        snap.Plan = "API key";
        snap.Windows.Add(new UsageWindow { Label = "Status", Detail = "Connected — Admin key untuk data spend" });
        return snap;
    }

    private static double SumCost(JsonElement root)
    {
        double total = 0;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var bucket in data.EnumerateArray())
            {
                if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) continue;
                foreach (var r in results.EnumerateArray())
                {
                    foreach (var name in new[] { "amount", "cost", "value" })
                    {
                        if (r.TryGetProperty(name, out var amt))
                        {
                            if (amt.ValueKind == JsonValueKind.Number) total += amt.GetDouble();
                            else if (amt.ValueKind == JsonValueKind.Object && amt.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
                                total += v.GetDouble();
                        }
                    }
                }
            }
        }
        return total;
    }
}
