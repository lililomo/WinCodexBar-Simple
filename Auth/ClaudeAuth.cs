using System.Text.Json;

namespace WinCodexBar.Auth;

/// <summary>
/// Claude (Pro/Max) browser login — mirrors the current Claude Code OAuth + PKCE flow.
/// [LOW–MEDIUM confidence] client id/endpoints are public but UNOFFICIAL. // VERIFY.
///
/// Uses an RFC 8252 loopback redirect (port-agnostic), so the browser returns straight
/// to the app after the user approves — no code to copy.
///
///   authorize: https://claude.ai/oauth/authorize          (PKCE S256, loopback redirect)
///   token:     https://platform.claude.com/v1/oauth/token (form-encoded)
/// </summary>
public static class ClaudeAuth
{
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string AuthorizeUrl = "https://claude.ai/oauth/authorize";
    private const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string Scopes = "user:profile user:inference user:sessions:claude_code user:mcp_servers";

    public static async Task<OAuthTokens> LoginAsync(CancellationToken ct)
    {
        var (verifier, challenge) = Pkce.Create();
        using var server = new LoopbackServer(0, "/callback");
        var state = Pkce.Base64Url(Guid.NewGuid().ToByteArray());
        var redirect = server.RedirectUri;

        // NOTE: do NOT send code=true here — it forces the "display the code" mode meant for the
        // console paste page and makes the server reject a loopback grant as "Invalid request format".
        var url = $"{AuthorizeUrl}?client_id={ClientId}&response_type=code"
                + $"&redirect_uri={Uri.EscapeDataString(redirect)}"
                + $"&scope={Uri.EscapeDataString(Scopes)}"
                + $"&code_challenge={challenge}&code_challenge_method=S256&state={state}";

        Browser.Open(url);
        var cb = await server.WaitForCallbackAsync(ct);

        if (cb.TryGetValue("error", out var err))
            throw new Exception($"otorisasi ditolak: {err}");
        if (!cb.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new Exception("tidak ada authorization code dari browser");

        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["state"] = cb.GetValueOrDefault("state") ?? state,
            ["redirect_uri"] = redirect,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(body) };
        using var res = await Net.Http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new Exception($"tukar token gagal ({(int)res.StatusCode}): {OAuthUtil.Trim(json)}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new OAuthTokens
        {
            AccessToken = OAuthUtil.Str(root, "access_token"),
            RefreshToken = OAuthUtil.Str(root, "refresh_token"),
            ExpiresAt = root.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.UtcNow.AddSeconds(ei.GetDouble())
                : null,
        };
    }
}
