using System.Text.Json;

namespace WinCodexBar.Providers;

/// <summary>
/// Optional: GitHub OAuth device flow so the user doesn't have to paste a token.
/// [MEDIUM confidence] — uses the well-known VS Code Copilot client_id. // VERIFY it still works.
/// Standard GitHub device flow:
///   POST https://github.com/login/device/code        -> user_code + verification_uri + device_code
///   (user visits the URL and types the code)
///   POST https://github.com/login/oauth/access_token  -> access_token (poll until granted)
/// </summary>
public static class CopilotDeviceFlow
{
    private const string ClientId = "Iv1.b507a08c87ecfe98"; // VS Code Copilot client id // VERIFY

    public sealed record DeviceStart(string DeviceCode, string UserCode, string VerificationUri, int IntervalSec);

    public static async Task<DeviceStart> StartAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code");
        req.Headers.Add("Accept", "application/json");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = "read:user",
        });

        using var res = await Net.Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        return new DeviceStart(
            r.GetProperty("device_code").GetString()!,
            r.GetProperty("user_code").GetString()!,
            r.GetProperty("verification_uri").GetString()!,
            r.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5);
    }

    /// <summary>Polls until the user authorises, then returns the access token. Throws on timeout/error.</summary>
    public static async Task<string> PollAsync(string deviceCode, int intervalSec, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(intervalSec, 5)), ct);

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
            req.Headers.Add("Accept", "application/json");
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            });

            using var res = await Net.Http.SendAsync(req, ct);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var r = doc.RootElement;

            if (r.TryGetProperty("access_token", out var at) && at.ValueKind == JsonValueKind.String)
                return at.GetString()!;

            var err = r.TryGetProperty("error", out var e) ? e.GetString() : null;
            if (err is "authorization_pending" or "slow_down")
                continue;
            throw new Exception($"device flow error: {err ?? "unknown"}");
        }
        throw new TimeoutException("device flow timed out");
    }
}
