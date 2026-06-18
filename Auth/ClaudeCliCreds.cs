using System.Text.Json;

namespace WinCodexBar.Auth;

/// <summary>
/// Reads the OAuth token the Claude Code CLI stores in ~/.claude/.credentials.json
/// — the same source CodexBar uses — so Claude usage works without a separate login.
/// </summary>
public static class ClaudeCliCreds
{
    public static string CredentialsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public sealed record Creds(string AccessToken, DateTimeOffset? ExpiresAt, string? Plan)
    {
        public bool Expired => ExpiresAt is { } e && e <= DateTimeOffset.UtcNow;
    }

    public static Creds? TryRead()
    {
        try
        {
            if (!File.Exists(CredentialsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o) || o.ValueKind != JsonValueKind.Object)
                return null;

            var token = OAuthUtil.Str(o, "accessToken");
            if (string.IsNullOrWhiteSpace(token)) return null;

            DateTimeOffset? exp = null;
            if (o.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number)
                exp = DateTimeOffset.FromUnixTimeMilliseconds(e.GetInt64());

            return new Creds(token!, exp, OAuthUtil.Str(o, "subscriptionType"));
        }
        catch
        {
            return null;
        }
    }
}
