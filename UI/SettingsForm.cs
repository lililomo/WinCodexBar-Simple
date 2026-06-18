using System.Drawing;

namespace WinCodexBar.UI;

/// <summary>Small settings dialog: transparency, always-on-top, and popup docking mode.</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly Action _onApply;

    private readonly TrackBar _opacity = new();
    private readonly Label _opacityLabel = new();
    private readonly CheckBox _alwaysOnTop = new();
    private readonly RadioButton _modeAnchored = new();
    private readonly RadioButton _modeFloating = new();

    public SettingsForm(AppConfig config, Action onApply)
    {
        _config = config;
        _onApply = onApply;

        Text = "WinCodexBar — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 250);
        Font = new Font("Segoe UI", 9f);

        Build();
        LoadValues();
    }

    private void Build()
    {
        int x = 18, y = 16, w = ClientSize.Width - 36;

        _opacityLabel.SetBounds(x, y, w, 20);
        Controls.Add(_opacityLabel);
        y += 24;

        _opacity.SetBounds(x, y, w, 40);
        _opacity.Minimum = 20;
        _opacity.Maximum = 100;
        _opacity.TickFrequency = 10;
        _opacity.SmallChange = 5;
        _opacity.LargeChange = 10;
        _opacity.ValueChanged += (_, _) => _opacityLabel.Text = $"Transparansi panel: {_opacity.Value}%";
        Controls.Add(_opacity);
        y += 50;

        _alwaysOnTop.SetBounds(x, y, w, 24);
        _alwaysOnTop.Text = "Selalu di atas (always on top)";
        Controls.Add(_alwaysOnTop);
        y += 32;

        var modeGroup = new GroupBox { Text = "Mode panel usage" };
        modeGroup.SetBounds(x, y, w, 76);
        _modeAnchored.SetBounds(12, 22, w - 30, 22);
        _modeAnchored.Text = "Nempel di tray (muncul saat ikon diklik)";
        _modeFloating.SetBounds(12, 46, w - 30, 22);
        _modeFloating.Text = "Floating bebas (bisa digeser, posisi diingat)";
        modeGroup.Controls.Add(_modeAnchored);
        modeGroup.Controls.Add(_modeFloating);
        Controls.Add(modeGroup);

        var save = new Button { Text = "Simpan", DialogResult = DialogResult.OK };
        save.SetBounds(ClientSize.Width - 190, ClientSize.Height - 38, 80, 26);
        save.Click += (_, _) => Apply();
        Controls.Add(save);

        var close = new Button { Text = "Tutup", DialogResult = DialogResult.Cancel };
        close.SetBounds(ClientSize.Width - 100, ClientSize.Height - 38, 80, 26);
        Controls.Add(close);

        AcceptButton = save;
        CancelButton = close;
    }

    private void LoadValues()
    {
        _opacity.Value = Math.Clamp(_config.Ui.Opacity, _opacity.Minimum, _opacity.Maximum);
        _opacityLabel.Text = $"Transparansi panel: {_opacity.Value}%";
        _alwaysOnTop.Checked = _config.Ui.AlwaysOnTop;
        _modeAnchored.Checked = _config.Ui.PopupMode == PopupMode.AnchoredToTray;
        _modeFloating.Checked = _config.Ui.PopupMode == PopupMode.Floating;
    }

    private void Apply()
    {
        _config.Ui.Opacity = _opacity.Value;
        _config.Ui.AlwaysOnTop = _alwaysOnTop.Checked;
        _config.Ui.PopupMode = _modeFloating.Checked ? PopupMode.Floating : PopupMode.AnchoredToTray;
        _config.Save();
        _onApply();
    }
}
