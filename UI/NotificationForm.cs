using System.Drawing;
using System.Drawing.Drawing2D;

namespace WinCodexBar.UI;

/// <summary>
/// A small themed on-screen banner shown in the bottom-right corner, used for reset alerts.
/// Self-drawn (not the OS toast system), so it renders reliably on Windows 11 without any
/// app registration. Auto-dismisses; click to close.
/// </summary>
internal sealed class NotificationForm : Form
{
    private readonly Theme _t;
    private readonly string _title;
    private readonly string _message;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 7000 };

    protected override bool ShowWithoutActivation => true;

    public NotificationForm(Theme theme, string title, string message)
    {
        _t = theme;
        _title = title;
        _message = message;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        Width = 400;
        Height = 120;
        BackColor = _t.Card;
        Font = new Font("Segoe UI", 9f);

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Left = wa.Right - Width - 16;
        Top = wa.Bottom - Height - 16;

        using (var path = Rounded(new Rectangle(0, 0, Width, Height), 12))
            Region = new Region(path);

        _timer.Tick += (_, _) => Close();
        _timer.Start();
        Click += (_, _) => Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(_t.Card))
        using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 12))
        {
            g.FillPath(bg, path);
            using var pen = new Pen(_t.Border);
            g.DrawPath(pen, path);
        }

        var accent = _t.ProviderAccent("claude");
        using (var acc = new SolidBrush(accent))
            g.FillRectangle(acc, 0, 18, 5, Height - 36);

        using var titleF = new Font("Segoe UI Semibold", 13.5f, FontStyle.Bold);
        using var msgF = new Font("Segoe UI", 11.5f);
        using var brFg = new SolidBrush(_t.Fg);
        using var brDim = new SolidBrush(_t.Dim);

        using (var dot = new SolidBrush(accent))
            g.FillEllipse(dot, 22, 23, 14, 14);
        g.DrawString(_title, titleF, brFg, 46, 18);
        g.DrawString(_message, msgF, brDim, new RectangleF(22, 52, Width - 40, 60));
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
