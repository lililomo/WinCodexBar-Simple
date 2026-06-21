using System.Threading;

namespace WinCodexBar;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single instance — a second launch exits immediately so we never double-poll the APIs.
        using var mutex = new Mutex(true, "WinCodexBar.SingleInstance.9F3A", out bool isNew);
        if (!isNew) return;

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayAppContext());

        GC.KeepAlive(mutex);
    }
}
