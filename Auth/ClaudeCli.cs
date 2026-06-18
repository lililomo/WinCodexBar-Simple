using System.Diagnostics;

namespace WinCodexBar.Auth;

/// <summary>
/// "Delegated refresh" — the same trick CodexBar uses. Claude's OAuth refresh endpoint is
/// Cloudflare/rate-limit protected, so instead of refreshing the token ourselves we run the
/// official Claude Code CLI, which refreshes its own (expired) token and writes it back to
/// ~/.claude/.credentials.json. We then re-read that file.
/// </summary>
public static class ClaudeCli
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    private static DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private static string? _cachedExe;

    /// <summary>Locates the claude executable: PATH, then bundled IDE-extension binaries.</summary>
    public static string? FindExe()
    {
        if (_cachedExe is not null && File.Exists(_cachedExe)) return _cachedExe;

        // 1. On PATH.
        foreach (var name in new[] { "claude.exe", "claude.cmd", "claude" })
        {
            var p = WhereInPath(name);
            if (p is not null) return _cachedExe = p;
        }

        // 2. Bundled with the VS Code / Cursor / Insiders Claude Code extension.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var extRoot in new[]
        {
            Path.Combine(home, ".vscode", "extensions"),
            Path.Combine(home, ".vscode-insiders", "extensions"),
            Path.Combine(home, ".cursor", "extensions"),
        })
        {
            var exe = NewestBundledBinary(extRoot);
            if (exe is not null) return _cachedExe = exe;
        }

        return null;
    }

    /// <summary>
    /// Runs a tiny non-interactive command so the CLI refreshes its expired token.
    /// Throttled to avoid spawning the heavy CLI repeatedly. Returns false when skipped.
    /// </summary>
    public static async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _lastAttempt < Cooldown) return false;
        _lastAttempt = DateTimeOffset.UtcNow;

        var exe = FindExe() ?? throw new Exception("CLI Claude Code tidak ditemukan untuk refresh token");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        // A trivial print request: forces an authenticated call, which refreshes the token.
        // (Even if the model call itself errors, the refresh has already happened.)
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("ping");
        psi.ArgumentList.Add("--max-turns");
        psi.ArgumentList.Add("1");

        using var proc = Process.Start(psi) ?? throw new Exception("gagal menjalankan CLI Claude Code");
        proc.StandardInput.Close(); // avoid the CLI's 3s wait for piped stdin
        _ = proc.StandardOutput.ReadToEndAsync();
        _ = proc.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { /* ignore */ } }

        return true;
    }

    private static string? WhereInPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full)) return full;
            }
            catch { /* skip malformed PATH entries */ }
        }
        return null;
    }

    private static string? NewestBundledBinary(string extensionsRoot)
    {
        try
        {
            if (!Directory.Exists(extensionsRoot)) return null;
            return Directory.GetDirectories(extensionsRoot, "anthropic.claude-code-*")
                .Select(d => Path.Combine(d, "resources", "native-binary", "claude.exe"))
                .Where(File.Exists)
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
