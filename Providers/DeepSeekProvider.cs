using System.Globalization;
using System.Text.Json;

namespace WinCodexBar.Providers;

/// <summary>
/// DeepSeek (the API platform). Like the OpenAI API, it exposes prepaid BALANCE/credits,
/// not subscription quota windows — so this card shows money left, not reset bars.
///
///   GET https://api.deepseek.com/user/balance   Auth: header  Authorization: Bearer &lt;api key&gt;
///   -> { is_available, balance_infos: [ { currency, total_balance, granted_balance, topped_up_balance } ] }
///
/// Configure the key in config.json (deepseek → ApiKey), via the tray "Login → DeepSeek" menu,
/// or the DEEPSEEK_API_KEY environment variable.
/// </summary>
public sealed class DeepSeekProvider : IUsageProvider
{
    public string Id => "deepseek";
    public string DisplayName => "DeepSeek";

    public bool IsLoggedIn(ProviderConfig cfg) =>
        !string.IsNullOrWhiteSpace(cfg.ApiKey)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));

    public async Task<ProviderSnapshot> FetchAsync(ProviderConfig cfg, CancellationToken ct)
    {
        var snap = new ProviderSnapshot { ProviderId = Id, DisplayName = DisplayName };

        var key = cfg.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            snap.Error = "Belum ada API key. Klik kanan ikon → Login → DeepSeek (atau isi di config.json).";
            return snap;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
        req.Headers.Add("Authorization", $"Bearer {key.Trim()}");
        req.Headers.Add("Accept", "application/json");

        using var res = await Net.Http.SendAsync(req, ct);
        if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized)
            throw new Exception("unauthorized — API key DeepSeek invalid");
        Net.EnsureOk(res);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        bool available = root.TryGetProperty("is_available", out var av) && av.ValueKind == JsonValueKind.True;
        snap.Plan = available ? "available" : "unavailable";

        if (root.TryGetProperty("balance_infos", out var infos) && infos.ValueKind == JsonValueKind.Array)
        {
            // Prefer the USD bucket if there is more than one currency.
            JsonElement chosen = default;
            bool found = false;
            foreach (var b in infos.EnumerateArray())
            {
                if (!found) { chosen = b; found = true; }
                if (b.TryGetProperty("currency", out var c) &&
                    string.Equals(c.GetString(), "USD", StringComparison.OrdinalIgnoreCase))
                {
                    chosen = b;
                    break;
                }
            }

            if (found)
            {
                var cur = Str(chosen, "currency");
                var total = Str(chosen, "total_balance");
                var granted = Str(chosen, "granted_balance");

                if (total is not null)
                    snap.Windows.Add(new UsageWindow { Label = "Saldo", Detail = Format(total, cur) });

                if (granted is not null &&
                    double.TryParse(granted, NumberStyles.Any, CultureInfo.InvariantCulture, out var g) && g > 0)
                    snap.Windows.Add(new UsageWindow { Label = "Granted", Detail = Format(granted, cur) });
            }
        }

        if (snap.Windows.Count == 0)
            snap.Error = "tidak ada info saldo (bentuk API mungkin berubah)";
        return snap;
    }

    private static string Format(string amount, string? currency)
    {
        var sym = currency switch { "USD" => "$", "CNY" => "¥", _ => "" };
        return sym.Length > 0 ? $"{sym}{amount}" : $"{amount} {currency}".Trim();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
