using System.Drawing;

namespace WinCodexBar.UI;

/// <summary>Tiny modal text-input dialog (WinForms has no built-in InputBox in C#).</summary>
internal static class Prompt
{
    public static string? Text(string title, string message)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            TopMost = true,
            ClientSize = new Size(440, 160),
            Font = new Font("Segoe UI", 9f),
        };

        var label = new Label { AutoSize = false };
        label.SetBounds(14, 12, 412, 56);
        label.Text = message;

        var input = new TextBox();
        input.SetBounds(14, 74, 412, 24);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        ok.SetBounds(256, 112, 80, 30);

        var cancel = new Button { Text = "Batal", DialogResult = DialogResult.Cancel };
        cancel.SetBounds(346, 112, 80, 30);

        form.Controls.AddRange(new Control[] { label, input, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK && input.Text.Trim().Length > 0
            ? input.Text.Trim()
            : null;
    }
}
