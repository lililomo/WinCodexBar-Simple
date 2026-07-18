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
    private DateTimeOffset? _lastFiveHourReset;

    // Rate-limit resilience: keep the last good snapshot, back off on 429, debounce spammy refreshes.
    private readonly Dictionary<string, ProviderSnapshot> _lastGood = new();
    private readonly Dictionary<string, DateTimeOffset> _cooldownUntil = new();
    private readonly Dictionary<string, DateTimeOffset> _lastFetch = new();

    public TrayAppContext()
    {
        _config = AppConfig.Load();
        _providers = new List<IUsageProvider>
        {
            new ClaudeProvider(),
            new ClaudeApiProvider(),
            new ChatGptProvider(),
            new CopilotProvider(),
            new DeepSeekProvider(),
        };

        // Make sure every provider has a config entry, and seed the display order.
        foreach (var p in _providers)
        {
            _config.For(p.Id);
            if (!_config.Ui.ProviderOrder.Contains(p.Id)) _config.Ui.ProviderOrder.Add(p.Id);
        }
        _config.Save();

        // Restore last-known usage so the panel shows numbers immediately (and during cooldowns).
        foreach (var kv in UsageCache.Load()) _lastGood[kv.Key] = kv.Value;

        _popup.Configure(_config, () => _config.Save(), () => RefreshAsync(), Quit);

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
        login.DropDownItems.Add("Claude API — tempel API key…", null, async (_, _) => await LoginClaudeApiAsync());
        login.DropDownItems.Add("ChatGPT / Codex…", null, async (_, _) => await LoginChatGptAsync());
        login.DropDownItems.Add("GitHub Copilot (device)…", null, async (_, _) => await LoginCopilotAsync());
        login.DropDownItems.Add("DeepSeek — tempel API key…", null, async (_, _) => await LoginDeepSeekAsync());
        menu.Items.Add(login);

        var logout = new ToolStripMenuItem("Logout");
        logout.DropDownItems.Add("Claude", null, async (_, _) => await LogoutAsync("claude"));
        logout.DropDownItems.Add("Claude API", null, async (_, _) => await LogoutAsync("claude-api"));
        logout.DropDownItems.Add("ChatGPT / Codex", null, async (_, _) => await LogoutAsync("chatgpt"));
        logout.DropDownItems.Add("GitHub Copilot", null, async (_, _) => await LogoutAsync("copilot"));
        logout.DropDownItems.Add("DeepSeek", null, async (_, _) => await LogoutAsync("deepseek"));
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

            // Show every provider that's checked; fetch the logged-in ones, and show a "please
            // log in" card for the rest so they don't silently vanish.
            var tasks = _providers
                .Where(p => _config.For(p.Id).Enabled)
                .Select(p => p.IsLoggedIn(_config.For(p.Id))
                    ? FetchOne(p, cts.Token)
                    : Task.FromResult(NotLoggedIn(p)))
                .ToList();

            var results = await Task.WhenAll(tasks);
            _snapshots = results.ToList();
            UsageCache.Save(_lastGood);

            CheckClaudeReset();
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

    private static ProviderSnapshot NotLoggedIn(IUsageProvider p) => new()
    {
        ProviderId = p.Id,
        DisplayName = p.DisplayName,
        NotLoggedIn = true,
        Error = $"Belum login — klik kanan ikon → Login → {p.DisplayName}.",
    };

    private async Task<ProviderSnapshot> FetchOne(IUsageProvider p, CancellationToken ct)
    {
        var id = p.Id;
        var now = DateTimeOffset.UtcNow;

        ProviderSnapshot Cached(string fallbackError) =>
            _lastGood.TryGetValue(id, out var c)
                ? c
                : new ProviderSnapshot { ProviderId = id, DisplayName = p.DisplayName, Error = fallbackError };

        // Backing off after a 429 — don't call the endpoint again until the cooldown passes.
        if (_cooldownUntil.TryGetValue(id, out var until) && now < until)
            return Cached($"dibatasi (429) · coba lagi {Eta(until)}");

        // Debounce: ignore rapid re-fetches (e.g. refresh-button spam) when we already have data.
        if (_lastFetch.TryGetValue(id, out var lf) && now - lf < TimeSpan.FromSeconds(10) && _lastGood.ContainsKey(id))
            return _lastGood[id];

        _lastFetch[id] = now;
        try
        {
            var snap = await p.FetchAsync(_config.For(id), ct);
            if (snap.Ok && snap.Windows.Count > 0)
            {
                _lastGood[id] = snap;
                _cooldownUntil.Remove(id);
                return snap;
            }
            return _lastGood.TryGetValue(id, out var good) ? good : snap; // keep last good on empty/error
        }
        catch (RateLimitException ex)
        {
            var delay = ex.RetryAfter ?? TimeSpan.FromMinutes(10);
            if (delay < TimeSpan.FromSeconds(15)) delay = TimeSpan.FromSeconds(15);
            if (delay > TimeSpan.FromMinutes(15)) delay = TimeSpan.FromMinutes(15); // cap so it recovers within 15m
            _cooldownUntil[id] = now + delay;
            return Cached($"dibatasi (429) · coba lagi {Eta(now + delay)}");
        }
        catch (Exception ex)
        {
            return Cached(ex.Message);
        }
    }

    private static string Eta(DateTimeOffset until)
    {
        var s = until - DateTimeOffset.UtcNow;
        if (s <= TimeSpan.Zero) return "segera";
        if (s.TotalMinutes >= 1) return $"{(int)Math.Ceiling(s.TotalMinutes)}m";
        return $"{(int)s.TotalSeconds}s";
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

    // Fires a notification when the Claude 5-hour session window rolls over (its resets_at jumps forward).
    private void CheckClaudeReset()
    {
        var claude = _snapshots.FirstOrDefault(s => s.ProviderId == "claude" && s.Ok);
        var reset = claude?.Windows.FirstOrDefault(w => w.Key == "five_hour")?.ResetsAt;
        if (reset is null) return;

        // A genuine reset jumps the window ~5 hours forward. The API's resets_at otherwise jitters
        // by well under a second between calls, so require a big forward jump to avoid false alarms.
        if (_lastFiveHourReset is DateTimeOffset prev
            && reset.Value > prev.AddMinutes(30)
            && _config.Ui.NotifyOnReset)
            NotifyReset();

        // Track the max seen (ignore the sub-second jitter that dips backward).
        if (_lastFiveHourReset is not DateTimeOffset last || reset.Value > last)
            _lastFiveHourReset = reset;
    }

    private void NotifyReset()
    {
        try
        {
            System.Media.SystemSounds.Exclamation.Play();

            // Reliable self-drawn banner (Win11 OS toasts are flaky for unregistered apps).
            var f = new NotificationForm(Theme.Get(_config.Ui.Theme), "Claude",
                "Sesi 5 jam sudah reset — kuota kembali penuh.");
            f.Show();

            // Also try the OS toast, as a bonus (shows in Action Center when it works).
            _tray.ShowBalloonTip(8000, "Claude", "Sesi 5 jam sudah reset — kuota kembali penuh.", ToolTipIcon.Info);
        }
        catch { /* notifications are best-effort */ }
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
        var list = _providers.Select(p => (p.Id, p.DisplayName)).ToList();
        using var f = new SettingsForm(_config, list, () =>
        {
            _popup.ApplyUi();
            if (_popup.Visible) _popup.Render(_snapshots);
            _ = RefreshAsync(); // re-fetch so newly-shown providers load and hidden ones drop
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

    private async Task LoginClaudeApiAsync()
    {
        var key = Prompt.Text(
            "Claude API — API key",
            "Tempel Anthropic API key.\n"
            + "• sk-ant-admin… → menampilkan spend (usage biaya)\n"
            + "• sk-ant-api… → hanya status \"connected\".");
        if (string.IsNullOrWhiteSpace(key)) return;

        _config.For("claude-api").ApiKey = key.Trim();
        _config.Save();
        await RefreshAsync();
        MessageBox.Show("API key Claude API tersimpan.", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task LoginDeepSeekAsync()
    {
        var key = Prompt.Text(
            "DeepSeek — API key",
            "Tempel API key DeepSeek (diawali sk-...).\n"
            + "Ambil di platform.deepseek.com → API keys.");
        if (string.IsNullOrWhiteSpace(key)) return;

        _config.For("deepseek").ApiKey = key.Trim();
        _config.Save();
        await RefreshAsync();
        MessageBox.Show("API key DeepSeek tersimpan.", "WinCodexBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
