using System.Diagnostics;

namespace WinCodexBar;

/// <summary>
/// Self-install: when the exe is named "…Setup…" or run with --install, it copies itself into
/// %LOCALAPPDATA%\Programs\WinCodexBar, adds a Start-menu shortcut, and launches. Per-user only,
/// so no administrator rights are needed.
/// </summary>
internal static class Installer
{
    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinCodexBar");

    public static bool ShouldInstall(string[] args)
    {
        if (args.Contains("--install")) return true;
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        return name.Contains("setup", StringComparison.OrdinalIgnoreCase);
    }

    public static void Run()
    {
        try
        {
            var src = Environment.ProcessPath ?? throw new Exception("tak bisa menemukan path exe");
            Directory.CreateDirectory(InstallDir);
            var dest = Path.Combine(InstallDir, "WinCodexBar.exe");
            if (!string.Equals(src, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(src, dest, overwrite: true);

            CreateShortcut(dest);

            MessageBox.Show(
                $"WinCodexBar berhasil dipasang.\n\nLokasi: {InstallDir}\nShortcut: Start Menu → WinCodexBar\n\n(tanpa hak admin — hanya untuk akun ini)",
                "WinCodexBar Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);

            Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal memasang: " + ex.Message, "WinCodexBar Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void CreateShortcut(string target)
    {
        try
        {
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinCodexBar.lnk");
            var script =
                $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{lnk}');" +
                $"$s.TargetPath='{target}';" +
                $"$s.WorkingDirectory='{Path.GetDirectoryName(target)}';" +
                "$s.Save()";

            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            Process.Start(psi)?.WaitForExit(10000);
        }
        catch { /* shortcut is best-effort */ }
    }
}
