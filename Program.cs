using System.Threading;

namespace WinCodexBar;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Setup mode: WinCodexBar-Setup.exe (or --install) installs per-user, then launches.
        if (Installer.ShouldInstall(args))
        {
            Installer.Run();
            return;
        }

        // Single instance — a second launch exits immediately so we never double-poll the APIs.
        using var mutex = new Mutex(true, "WinCodexBar.SingleInstance.9F3A", out bool isNew);
        if (!isNew) return;

        Application.Run(new TrayAppContext());
        GC.KeepAlive(mutex);
    }
}
