using System.Drawing;

namespace WinCodexBar.UI;

/// <summary>Settings dialog: theme, which AIs to show, Claude cookie source, transparency, on-top, popup mode, close behavior.</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly Action _onApply;
    private readonly IReadOnlyList<(string Id, string Name)> _providers;

    private readonly ComboBox _theme = new();
    private readonly Dictionary<string, CheckBox> _show = new();
    private readonly RadioButton _claudeManual = new();
    private readonly RadioButton _claudeAuto = new();
    private readonly ComboBox _claudeBrowser = new();
    private readonly TrackBar _opacity = new();
    private readonly Label _opacityLabel = new();
    private readonly CheckBox _alwaysOnTop = new();
    private readonly CheckBox _notifyReset = new();
    private readonly RadioButton _modeAnchored = new();
    private readonly RadioButton _modeFloating = new();
    private readonly RadioButton _closeHide = new();
    private readonly RadioButton _closeQuit = new();

    public SettingsForm(AppConfig config, IReadOnlyList<(string Id, string Name)> providers, Action onApply)
    {
        _config = config;
        _providers = providers;
        _onApply = onApply;

        Text = "WinCodexBar — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(390, 598 + _providers.Count * 24);

        Build();
        LoadValues();
    }

    private void Build()
    {
        int x = 18, y = 16, w = ClientSize.Width - 36;

        Controls.Add(new Label { Text = "Theme", Bounds = new Rectangle(x, y, w, 18) });
        y += 22;
        _theme.SetBounds(x, y, w, 26);
        _theme.DropDownStyle = ComboBoxStyle.DropDownList;
        _theme.Items.AddRange(Theme.Names);
        Controls.Add(_theme);
        y += 40;

        // Which AIs to show
        int showH = 26 + _providers.Count * 24 + 8;
        var showGroup = new GroupBox { Text = "Tampilkan AI (yang dicentang saja yang muncul)", Bounds = new Rectangle(x, y, w, showH) };
        int cyy = 22;
        foreach (var (id, name) in _providers)
        {
            var cb = new CheckBox { Text = name, Bounds = new Rectangle(12, cyy, w - 30, 22) };
            _show[id] = cb;
            showGroup.Controls.Add(cb);
            cyy += 24;
        }
        Controls.Add(showGroup);
        y += showH + 12;

        // Claude cookie source
        var ck = new GroupBox { Text = "Cookie Claude (sumber sessionKey)", Bounds = new Rectangle(x, y, w, 98) };
        _claudeManual.SetBounds(12, 20, w - 30, 22);
        _claudeManual.Text = "Manual (tempel sessionKey lewat menu)";
        _claudeAuto.SetBounds(12, 44, 120, 24);
        _claudeAuto.Text = "Otomatis dari:";
        _claudeBrowser.SetBounds(136, 44, w - 166, 24);
        _claudeBrowser.DropDownStyle = ComboBoxStyle.DropDownList;
        _claudeBrowser.Items.AddRange(new object[] { "Auto", "Firefox", "Chrome" });
        var note = new Label
        {
            Text = "Otomatis membaca cookie login claude.ai dari browser.",
            Bounds = new Rectangle(12, 72, w - 24, 18),
            ForeColor = SystemColors.GrayText,
        };
        _claudeAuto.CheckedChanged += (_, _) => _claudeBrowser.Enabled = _claudeAuto.Checked;
        ck.Controls.AddRange(new Control[] { _claudeManual, _claudeAuto, _claudeBrowser, note });
        Controls.Add(ck);
        y += 108;

        _opacityLabel.SetBounds(x, y, w, 18);
        Controls.Add(_opacityLabel);
        y += 22;
        _opacity.SetBounds(x, y, w, 40);
        _opacity.Minimum = 20; _opacity.Maximum = 100; _opacity.TickFrequency = 10;
        _opacity.SmallChange = 5; _opacity.LargeChange = 10;
        _opacity.ValueChanged += (_, _) => _opacityLabel.Text = $"Transparansi panel: {_opacity.Value}%";
        Controls.Add(_opacity);
        y += 50;

        _alwaysOnTop.SetBounds(x, y, w, 24);
        _alwaysOnTop.Text = "Selalu di atas (always on top)";
        Controls.Add(_alwaysOnTop);
        y += 28;

        _notifyReset.SetBounds(x, y, w, 24);
        _notifyReset.Text = "Notifikasi + suara saat sesi Claude 5 jam reset";
        Controls.Add(_notifyReset);
        y += 32;

        var modeGroup = new GroupBox { Text = "Mode panel usage", Bounds = new Rectangle(x, y, w, 76) };
        _modeAnchored.SetBounds(12, 22, w - 30, 22);
        _modeAnchored.Text = "Nempel di tray (muncul saat ikon diklik)";
        _modeFloating.SetBounds(12, 46, w - 30, 22);
        _modeFloating.Text = "Floating bebas (digeser, posisi diingat)";
        modeGroup.Controls.Add(_modeAnchored);
        modeGroup.Controls.Add(_modeFloating);
        Controls.Add(modeGroup);
        y += 86;

        var closeGroup = new GroupBox { Text = "Tombol close (×) pada panel", Bounds = new Rectangle(x, y, w, 76) };
        _closeHide.SetBounds(12, 22, w - 30, 22);
        _closeHide.Text = "Tutup jendelanya saja (app tetap jalan)";
        _closeQuit.SetBounds(12, 46, w - 30, 22);
        _closeQuit.Text = "Keluar / matikan aplikasi";
        closeGroup.Controls.Add(_closeHide);
        closeGroup.Controls.Add(_closeQuit);
        Controls.Add(closeGroup);

        var save = new Button { Text = "Simpan", DialogResult = DialogResult.OK };
        save.SetBounds(ClientSize.Width - 190, ClientSize.Height - 36, 80, 26);
        save.Click += (_, _) => Apply();
        Controls.Add(save);

        var close = new Button { Text = "Tutup", DialogResult = DialogResult.Cancel };
        close.SetBounds(ClientSize.Width - 100, ClientSize.Height - 36, 80, 26);
        Controls.Add(close);

        AcceptButton = save;
        CancelButton = close;
    }

    private void LoadValues()
    {
        _theme.SelectedItem = Theme.Names.Contains(_config.Ui.Theme) ? _config.Ui.Theme : "Midnight";
        foreach (var (id, cb) in _show) cb.Checked = _config.For(id).Enabled;

        var cs = _config.For("claude").CookieSource;
        bool manual = string.IsNullOrEmpty(cs) || string.Equals(cs, "Manual", StringComparison.OrdinalIgnoreCase);
        _claudeManual.Checked = manual;
        _claudeAuto.Checked = !manual;
        _claudeBrowser.SelectedItem = new[] { "Auto", "Firefox", "Chrome" }.Contains(cs) ? cs : "Auto";
        _claudeBrowser.Enabled = _claudeAuto.Checked;

        _opacity.Value = Math.Clamp(_config.Ui.Opacity, _opacity.Minimum, _opacity.Maximum);
        _opacityLabel.Text = $"Transparansi panel: {_opacity.Value}%";
        _alwaysOnTop.Checked = _config.Ui.AlwaysOnTop;
        _notifyReset.Checked = _config.Ui.NotifyOnReset;
        _modeAnchored.Checked = _config.Ui.PopupMode == PopupMode.AnchoredToTray;
        _modeFloating.Checked = _config.Ui.PopupMode == PopupMode.Floating;
        _closeHide.Checked = _config.Ui.CloseButton == CloseAction.HideWindow;
        _closeQuit.Checked = _config.Ui.CloseButton == CloseAction.QuitApp;
    }

    private void Apply()
    {
        _config.Ui.Theme = _theme.SelectedItem as string ?? "Midnight";
        foreach (var (id, cb) in _show) _config.For(id).Enabled = cb.Checked;
        _config.For("claude").CookieSource = _claudeAuto.Checked ? (_claudeBrowser.SelectedItem as string ?? "Auto") : "Manual";
        _config.Ui.Opacity = _opacity.Value;
        _config.Ui.AlwaysOnTop = _alwaysOnTop.Checked;
        _config.Ui.NotifyOnReset = _notifyReset.Checked;
        _config.Ui.PopupMode = _modeFloating.Checked ? PopupMode.Floating : PopupMode.AnchoredToTray;
        _config.Ui.CloseButton = _closeQuit.Checked ? CloseAction.QuitApp : CloseAction.HideWindow;
        _config.Save();
        _onApply();
    }
}
