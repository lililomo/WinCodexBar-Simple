using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WinCodexBar.Auth;

/// <summary>PKCE (RFC 7636) code verifier/challenge with S256.</summary>
internal static class Pkce
{
    public static (string verifier, string challenge) Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return (verifier, Base64Url(hash));
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal static class Browser
{
    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* if it fails the user can still copy the URL from the dialog */ }
    }
}

/// <summary>
/// Minimal loopback HTTP server for an OAuth redirect (RFC 8252).
/// Uses a raw TcpListener so it works without admin / URL-ACL reservations,
/// unlike HttpListener.
/// </summary>
internal sealed class LoopbackServer : IDisposable
{
    private readonly TcpListener _listener;
    public int Port { get; }
    public string CallbackPath { get; }

    /// <param name="port">Fixed port, or 0 to let the OS pick a free one.</param>
    public LoopbackServer(int port, string callbackPath)
    {
        CallbackPath = callbackPath;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public string RedirectUri => $"http://localhost:{Port}{CallbackPath}";

    /// <summary>Waits for the browser redirect and returns the parsed query string.</summary>
    public async Task<Dictionary<string, string>> WaitForCallbackAsync(CancellationToken ct)
    {
        using var client = await _listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();

        var buffer = new byte[8192];
        int n = await stream.ReadAsync(buffer, ct);
        var request = Encoding.ASCII.GetString(buffer, 0, n);

        // Request line looks like:  GET /callback?code=...&state=... HTTP/1.1
        var firstLine = request.Split("\r\n", 2)[0];
        var target = firstLine.Split(' ').ElementAtOrDefault(1) ?? "/";
        var query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";

        const string html =
            "<!doctype html><meta charset=utf-8>"
            + "<body style=\"font-family:Segoe UI,sans-serif;background:#1c1c20;color:#eaeaea;"
            + "text-align:center;padding-top:80px\">"
            + "<h2>WinCodexBar</h2><p>Login berhasil. Anda boleh menutup tab ini dan kembali ke aplikasi.</p></body>";
        var resp = "HTTP/1.1 200 OK\r\n"
                 + "Content-Type: text/html; charset=utf-8\r\n"
                 + "Connection: close\r\n"
                 + $"Content-Length: {Encoding.UTF8.GetByteCount(html)}\r\n\r\n"
                 + html;
        await stream.WriteAsync(Encoding.UTF8.GetBytes(resp), ct);

        return ParseQuery(query);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            if (i < 0) continue;
            dict[WebUtility.UrlDecode(pair[..i])] = WebUtility.UrlDecode(pair[(i + 1)..]);
        }
        return dict;
    }

    public void Dispose() => _listener.Stop();
}

/// <summary>Small helpers shared by the OAuth providers.</summary>
internal static class OAuthUtil
{
    public static string? Str(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString()
            : null;

    public static string Trim(string s) => s.Length > 240 ? s[..240] + "…" : s;

    /// <summary>Decodes the payload (middle segment) of a JWT into a JsonDocument, or null.</summary>
    public static System.Text.Json.JsonDocument? DecodeJwtPayload(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(payload);
            return System.Text.Json.JsonDocument.Parse(bytes);
        }
        catch
        {
            return null;
        }
    }
}
