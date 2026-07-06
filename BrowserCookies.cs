using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WinCodexBar;

/// <summary>
/// Reads the claude.ai cookies straight from the browser's cookie store (like CodexBar), so the
/// Claude usage endpoint can be called with the browser's own session — no manual paste, and it
/// includes cf_clearance so Cloudflare lets the request through.
///
/// Firefox stores cookie values in plaintext; Chrome encrypts them with AES-GCM using a
/// DPAPI-protected key. Returns a full "name=value; …" Cookie header for claude.ai.
/// </summary>
internal static class BrowserCookies
{
    // cf_clearance is bound to the User-Agent, so each browser's cookies must be sent with a
    // matching UA. (Verified: a Firefox UA is accepted with Firefox cookies.)
    private const string FirefoxUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:131.0) Gecko/20100101 Firefox/131.0";
    private const string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public sealed record CookieSet(string Header, string UserAgent);

    public static CookieSet? ClaudeCookies(string? source) => (source ?? "").Trim() switch
    {
        "Firefox" => Wrap(Firefox(), FirefoxUA),
        "Chrome" => Wrap(Chrome(), ChromeUA),
        "Auto" => Wrap(Firefox(), FirefoxUA) ?? Wrap(Chrome(), ChromeUA),
        _ => null,
    };

    private static CookieSet? Wrap(string? header, string ua) =>
        string.IsNullOrWhiteSpace(header) ? null : new CookieSet(header!, ua);

    public static string Display(string? source) => (source ?? "").Trim() switch
    {
        "Auto" => "browser (auto)",
        "Firefox" => "Firefox",
        "Chrome" => "Chrome",
        _ => "manual",
    };

    private static string BuildHeader(List<KeyValuePair<string, string>> cookies) =>
        string.Join("; ", cookies.Where(c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));

    // ---- Firefox (plaintext values) -----------------------------------------
    private static string? Firefox()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(root)) return null;
        foreach (var prof in Directory.GetDirectories(root))
        {
            var db = Path.Combine(prof, "cookies.sqlite");
            if (!File.Exists(db)) continue;
            var cookies = ReadSqlite(db, "SELECT name, value FROM moz_cookies WHERE host LIKE '%claude.ai%'", encrypted: false, key: null);
            if (cookies.Any(c => c.Key == "sessionKey")) return BuildHeader(cookies);
        }
        return null;
    }

    // ---- Chrome (AES-GCM encrypted values) ----------------------------------
    private static string? Chrome()
    {
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data");
        if (!Directory.Exists(userData)) return null;
        var key = ChromeKey(Path.Combine(userData, "Local State"));
        if (key is null) return null;

        foreach (var prof in new[] { "Default", "Profile 1", "Profile 2", "Profile 3" })
        {
            foreach (var rel in new[] { Path.Combine(prof, "Network", "Cookies"), Path.Combine(prof, "Cookies") })
            {
                var db = Path.Combine(userData, rel);
                if (!File.Exists(db)) continue;
                var cookies = ReadSqlite(db, "SELECT name, encrypted_value FROM cookies WHERE host_key LIKE '%claude.ai%'", encrypted: true, key: key);
                if (cookies.Any(c => c.Key == "sessionKey")) return BuildHeader(cookies);
            }
        }
        return null;
    }

    private static byte[]? ChromeKey(string localState)
    {
        try
        {
            if (!File.Exists(localState)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(localState));
            var b64 = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString();
            var blob = Convert.FromBase64String(b64!);
            if (blob.Length > 5 && Encoding.ASCII.GetString(blob, 0, 5) == "DPAPI")
                return ProtectedData.Unprotect(blob[5..], null, DataProtectionScope.CurrentUser);
        }
        catch { /* app-bound / unsupported */ }
        return null;
    }

    private static string? ChromeDecrypt(byte[] enc, byte[] key)
    {
        try
        {
            if (enc.Length < 31) return null;
            var prefix = Encoding.ASCII.GetString(enc, 0, 3);
            if (prefix is "v10" or "v11")
            {
                var nonce = enc.AsSpan(3, 12);
                var tag = enc.AsSpan(enc.Length - 16, 16);
                var cipher = enc.AsSpan(15, enc.Length - 15 - 16);
                var plain = new byte[cipher.Length];
                using var gcm = new AesGcm(key, 16);
                gcm.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            return null; // "v20" app-bound encryption isn't decryptable outside Chrome
        }
        catch { return null; }
    }

    private static List<KeyValuePair<string, string>> ReadSqlite(string dbPath, string sql, bool encrypted, byte[]? key)
    {
        var result = new List<KeyValuePair<string, string>>();
        var tmp = Path.Combine(Path.GetTempPath(), "wcb_ck_" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            File.Copy(dbPath, tmp, true); // the browser may hold a lock; work on a copy
            foreach (var ext in new[] { "-wal", "-shm" })
                if (File.Exists(dbPath + ext)) File.Copy(dbPath + ext, tmp + ext, true);

            using var conn = new SqliteConnection($"Data Source={tmp}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(1)) continue;
                var name = r.GetString(0);
                var value = encrypted ? ChromeDecrypt(r.GetFieldValue<byte[]>(1), key!) : r.GetString(1);
                if (!string.IsNullOrEmpty(value)) result.Add(new KeyValuePair<string, string>(name, value!));
            }
        }
        catch { /* locked / unreadable */ }
        finally
        {
            try
            {
                File.Delete(tmp);
                foreach (var ext in new[] { "-wal", "-shm" }) if (File.Exists(tmp + ext)) File.Delete(tmp + ext);
            }
            catch { }
        }
        return result;
    }
}
