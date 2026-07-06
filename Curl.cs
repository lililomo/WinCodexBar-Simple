using System.Diagnostics;

namespace WinCodexBar;

/// <summary>
/// Minimal GET via the bundled Windows <c>curl.exe</c>. Claude's claude.ai endpoints sit behind a
/// Cloudflare check that blocks the .NET HttpClient TLS fingerprint (403) but lets curl through,
/// so browser-cookie requests are made with curl instead.
/// </summary>
internal static class Curl
{
    public static readonly string? ExePath = Resolve();

    private static string? Resolve()
    {
        var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
        if (File.Exists(sys)) return sys;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var d in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var f = Path.Combine(d.Trim(), "curl.exe"); if (File.Exists(f)) return f; }
            catch { /* skip bad PATH entry */ }
        }
        return null;
    }

    public static async Task<(int status, string body)> GetAsync(string url, string cookieHeader, string userAgent, CancellationToken ct)
    {
        var exe = ExePath ?? throw new Exception("curl.exe tidak ditemukan (butuh Windows 10 1803+)");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[]
        {
            "-s", "--compressed",
            "-A", userAgent,
            "-b", cookieHeader,
            "-H", "Accept: application/json",
            "-w", "\n%{http_code}",
            url,
        })
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new Exception("gagal menjalankan curl.exe");
        var outTask = proc.StandardOutput.ReadToEndAsync();
        _ = proc.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { } throw new Exception("curl timeout"); }

        var outp = await outTask;
        int status = 0;
        var body = outp;
        var nl = outp.LastIndexOf('\n');
        if (nl >= 0)
        {
            int.TryParse(outp[(nl + 1)..].Trim(), out status);
            body = outp[..nl];
        }
        return (status, body);
    }
}
