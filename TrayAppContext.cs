using System.Diagnostics;
using WinCodexBar.Auth;
using WinCodexBar.Providers;
using WinCodexBar.UI;

namespace WinCodexBar;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AppConfig _config;
    private readonly List<IUsageProvider> _providers;
    private readonly PopupForm _popup = new();

    private List<ProviderSnapshot> _snapshots = new();
    private bool _refreshing;

    public TrayAppContext()
    {
        _config = AppConfig.Load();
        _providers = new List<IUsageProvider>
        {
            new ClaudeProvider(),
            new ChatGptProvider(),
            new CopilotProvider(),
        };

        // Make sure every provider has a config entry, then persist defaults.
        foreach (var p in _providers) _config.For(p.Id);
        _config.Save();

        _popup.Configure(_config, () => _config.Save(), () => RefreshAsync());

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "WinCodexBar",
            Icon = IconFactory.Build(null),
            ContextMenuStrip = BuildMenu(),
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) TogglePopup();
        };

        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(30, _config.RefreshSeconds) * 1000,
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        menu.Items.Add("Show usage", null, (_, _) => ShowPopup());
        menu.Items.Add(new ToolStripSeparator());

        var login = new ToolStripMenuItem("Login");
        login.DropDownItems.Add("Claude — login browser (OAuth)…", null, async (_, _) => await LoginClaudeAsync());
        login.DropDownItems.Add("Claude — tempel sessionKey…", null, async (_, _) => await LoginClaudeSessionKeyAsync());
        login.DropDownItems.Add("ChatGPT / Codex…", null, async (_, _) => await LoginChatGptAsync());
        login.DropDownItems.Add("GitHub Copilot (device)…", null, async (_, _) => await LoginCopilotAsync());
        menu.Items.Add(login);

        var logout = new ToolStripMenuItem("Logout");
        logout.DropDownItems.Add("Claude", null, async (_, _) => await LogoutAsync("claude"));
        logout.DropDownItems.Add("ChatGPT / Codex", null, async (_, _) => await LogoutAsync("chatgpt"));
        logout.DropDownItems.Add("GitHub Copilot", null, async (_, _) => await LogoutAsync("copilot"));
        menu.Items.Add(logout);

        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Open config folder", null, (_, _) => OpenInShell(AppConfig.Dir));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        return menu;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Only fetch (and therefore only show) providers that are logged in.
            var tasks = _providers
                .Where(p => p.IsLoggedIn(_config.For(p.Id)))
                .Select(p => SafeFetch(p, _config.For(p.Id), cts.Token))
                .ToList();

            var results = await Task.WhenAll(tasks);
            _snapshots = results.ToList();

            UpdateIcon();
            if (_popup.Visible) _popup.Render(_snapshots);
        }
        catch
        {
            // Never let a refresh crash the tray.
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static async Task<ProviderSnapshot> SafeFetch(IUsageProvider p, ProviderConfig cfg, CancellationToken ct)
    {
        try
        {
            return await p.FetchAsync(cfg, ct);
        }
        catch (Exception ex)
        {
            return new ProviderSnapshot { ProviderId = p.Id, DisplayName = p.DisplayName, Error = ex.Message };
        }
    }

    private void UpdateIcon()
    {
        var measured = _snapshots
            .Where(s => s.Ok && s.PeakUtilization is not null)
            .Select(s => s.PeakUtilization!.Value)
            .ToList();
        double? peak = measured.Count == 0 ? null : measured.Max();

        var old = _tray.Icon;
        _tray.Icon = IconFactory.Build(peak);
        old?.Dispose();

        if (_snapshots.Count == 0)
        {
            _tray.Text = "WinCodexBar — belum ada login";
            return;
        }

        var lines = _snapshots.Select(s =>
            s.Ok
                ? $"{s.DisplayName}: {(s.PeakUtilization is double u ? $"{u * 100:0}%" : "ok")}"
                : $"{s.DisplayName}: error");
        var text = string.Join("\n", lines);
        _tray.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon.Text hard limit
    }

    private void TogglePopup()
    {
        if (_popup.Visible) _popup.Hide();
        else ShowPopup();
    }

    private void ShowPopup()
    {
        _popup.ApplyUi();
        _popup.Render(_snapshots);
        _popup.Show();
        _popup.Activate();
    }

    private void ShowSettings()
    {
        using var f = new SettingsForm(_config, () =>
        {
            _popup.ApplyUi();
            if (_popup.Visible) _popup.Render(_snapshots);
        });
        f.ShowDialog();
    }

    private async Task LoginClaudeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var tokens = await ClaudeAuth.LoginAsync(cts.Token);
            _config.For("claude").OAuth = tokens;
            _config.Save();
            await RefreshAsync();
            MessageBox.Show("Claude tersambung.", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Login Claude gagal: {ex.Message}", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task LoginClaudeSessionKeyAsync()
    {
        var key = Prompt.Text(
            "Claude — sessionKey",
            "Tempel nilai cookie \"sessionKey\" dari claude.ai (diawali sk-ant-sid01-...).\n"
            + "Ambil lewat: buka claude.ai → F12 → Application → Cookies → claude.ai → sessionKey.");
        if (string.IsNullOrWhiteSpace(key)) return;

        _config.For("claude").SessionKey = key.Trim();
        _config.Save();
        await RefreshAsync();
        MessageBox.Show("sessionKey Claude tersimpan.", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task LoginChatGptAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var tokens = await ChatGptAuth.LoginAsync(cts.Token);
            _config.For("chatgpt").OAuth = tokens;
            _config.Save();
            await RefreshAsync();
            MessageBox.Show("ChatGPT / Codex tersambung.", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Login ChatGPT gagal: {ex.Message}", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task LoginCopilotAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            var start = await CopilotDeviceFlow.StartAsync(cts.Token);

            OpenInShell(start.VerificationUri);
            MessageBox.Show(
                $"Buka {start.VerificationUri} lalu masukkan kode ini:\n\n{start.UserCode}\n\nKlik OK setelah konfirmasi; "
                + "WinCodexBar akan terus menunggu.",
                "Login Copilot (device flow)", MessageBoxButtons.OK, MessageBoxIcon.Information);

            var token = await CopilotDeviceFlow.PollAsync(start.DeviceCode, start.IntervalSec, cts.Token);
            _config.For("copilot").Token = token;
            _config.Save();
            await RefreshAsync();

            MessageBox.Show("Copilot tersambung.", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Login gagal: {ex.Message}", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task LogoutAsync(string id)
    {
        var c = _config.For(id);
        c.OAuth = null;
        c.SessionKey = null;
        c.Token = null;
        c.ApiKey = null;
        _config.Save();
        await RefreshAsync();
    }

    private static void OpenInShell(string path)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Quit()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _popup.Dispose();
        ExitThread();
    }
}
