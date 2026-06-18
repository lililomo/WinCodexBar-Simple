using System.Text.Json;

namespace WinCodexBar.Auth;

/// <summary>
/// "Sign in with ChatGPT" browser login — mirrors the Codex CLI OAuth + PKCE flow.
/// This is the ChatGPT/Codex SUBSCRIPTION login, NOT the OpenAI API.
/// [LOW–MEDIUM confidence] client id + endpoints are public but UNOFFICIAL. // VERIFY.
///
///   authorize: https://auth.openai.com/oauth/authorize  (PKCE S256, loopback :1455)
///   token:     https://auth.openai.com/oauth/token
/// </summary>
public static class ChatGptAuth
{
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string AuthorizeUrl = "https://auth.openai.com/oauth/authorize";
    private const string TokenUrl = "https://auth.openai.com/oauth/token";
    private const int Port = 1455;
    private const string Scopes = "openid profile email offline_access";

    public static async Task<OAuthTokens> LoginAsync(CancellationToken ct)
    {
        var (verifier, challenge) = Pkce.Create();
        using var server = new LoopbackServer(Port, "/auth/callback");
        var state = Pkce.Base64Url(Guid.NewGuid().ToByteArray());

        var url = $"{AuthorizeUrl}?response_type=code&client_id={ClientId}"
                + $"&redirect_uri={Uri.EscapeDataString(server.RedirectUri)}"
                + $"&scope={Uri.EscapeDataString(Scopes)}"
                + $"&code_challenge={challenge}&code_challenge_method=S256&state={state}"
                + "&id_token_add_organizations=true&codex_cli_simplified_flow=true";

        Browser.Open(url);
        var cb = await server.WaitForCallbackAsync(ct);

        if (cb.TryGetValue("error", out var err))
            throw new Exception($"otorisasi ditolak: {err}");
        if (!cb.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new Exception("tidak ada authorization code dari browser");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = server.RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        using var res = await Net.Http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new Exception($"tukar token gagal ({(int)res.StatusCode}): {OAuthUtil.Trim(json)}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tokens = new OAuthTokens
        {
            AccessToken = OAuthUtil.Str(root, "access_token"),
            RefreshToken = OAuthUtil.Str(root, "refresh_token"),
            IdToken = OAuthUtil.Str(root, "id_token"),
            ExpiresAt = root.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.UtcNow.AddSeconds(ei.GetDouble())
                : null,
        };
        (tokens.AccountId, _) = ReadIdToken(tokens.IdToken);
        return tokens;
    }

    /// <summary>Pulls the chatgpt account id + plan type out of the id_token JWT, when present.</summary>
    public static (string? accountId, string? plan) ReadIdToken(string? idToken)
    {
        using var doc = OAuthUtil.DecodeJwtPayload(idToken);
        if (doc is null) return (null, null);

        // Claims live under the "https://api.openai.com/auth" namespace.
        if (doc.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth) &&
            auth.ValueKind == JsonValueKind.Object)
        {
            return (OAuthUtil.Str(auth, "chatgpt_account_id"), OAuthUtil.Str(auth, "chatgpt_plan_type"));
        }
        return (null, null);
    }
}
