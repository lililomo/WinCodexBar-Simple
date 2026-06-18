using System.Drawing;
using System.Drawing.Drawing2D;

namespace WinCodexBar.UI;

internal sealed class PopupForm : Form
{
    private List<ProviderSnapshot> _snapshots = new();

    private AppConfig? _cfg;
    private Action? _save;
    private Func<Task>? _refresh;

    private readonly Label _refreshBtn = new();

    private bool _dragging;
    private Point _dragOffset;

    private static readonly Color Bg = Color.FromArgb(28, 28, 32);
    private static readonly Color Fg = Color.FromArgb(230, 230, 232);
    private static readonly Color Dim = Color.FromArgb(138, 138, 147);
    private static readonly Color Track = Color.FromArgb(52, 52, 58);

    private const int W = 320;
    private const int Pad = 16;

    public PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        Width = W;
        Height = 200;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);

        // Manual refresh button in the top-right corner.
        _refreshBtn.Text = "⟳"; // ⟳
        _refreshBtn.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        _refreshBtn.ForeColor = Dim;
        _refreshBtn.BackColor = Bg;
        _refreshBtn.AutoSize = false;
        _refreshBtn.Size = new Size(26, 26);
        _refreshBtn.TextAlign = ContentAlignment.MiddleCenter;
        _refreshBtn.Cursor = Cursors.Hand;
        _refreshBtn.Location = new Point(W - _refreshBtn.Width - 6, 6);
        _refreshBtn.MouseEnter += (_, _) => _refreshBtn.ForeColor = Fg;
        _refreshBtn.MouseLeave += (_, _) => _refreshBtn.ForeColor = Dim;
        _refreshBtn.Click += async (_, _) =>
        {
            if (_refresh is null) return;
            try { await _refresh(); } catch { /* refresh guards itself */ }
        };
        Controls.Add(_refreshBtn);
        _refreshBtn.BringToFront();
    }

    public void Configure(AppConfig cfg, Action save, Func<Task> refresh)
    {
        _cfg = cfg;
        _save = save;
        _refresh = refresh;
    }

    /// <summary>Applies the user's look &amp; feel settings (opacity, always-on-top).</summary>
    public void ApplyUi()
    {
        if (_cfg is null) return;
        Opacity = Math.Clamp(_cfg.Ui.Opacity, 20, 100) / 100.0;
        TopMost = _cfg.Ui.AlwaysOnTop;
    }

    public void Render(List<ProviderSnapshot> snapshots)
    {
        _snapshots = snapshots;
        Height = Math.Max(120, Measure());
        Reposition();
        Invalidate();
    }

    private bool Floating => _cfg?.Ui.PopupMode == PopupMode.Floating;

    private void Reposition()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);

        if (Floating && _cfg is { Ui.FloatingX: >= 0, Ui.FloatingY: >= 0 })
        {
            // Restore the saved position, clamped so it can't end up off-screen.
            Left = Math.Clamp(_cfg.Ui.FloatingX, wa.Left, Math.Max(wa.Left, wa.Right - Width));
            Top = Math.Clamp(_cfg.Ui.FloatingY, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
            return;
        }

        // Anchored (or floating with no saved position yet): bottom-right near the tray.
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // A floating panel stays put; only the tray-anchored flyout auto-hides.
        if (!Floating) Hide();
    }

    // --- dragging (floating mode only) ---------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Floating && e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragOffset = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Left += e.X - _dragOffset.X;
            Top += e.Y - _dragOffset.Y;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            if (_cfg is not null)
            {
                _cfg.Ui.FloatingX = Left;
                _cfg.Ui.FloatingY = Top;
                _save?.Invoke();
            }
        }
    }

    private int Measure()
    {
        if (_snapshots.Count == 0) return 96;

        int h = Pad;
        foreach (var s in _snapshots)
        {
            h += 26;                                   // provider header
            if (!s.Ok) { h += 34; }                    // error block
            else if (s.Windows.Count == 0) { h += 20; }
            else { h += s.Windows.Count * 36; }
            h += 12;                                    // gap
        }
        return h + Pad;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Bg);

        using var bold = new Font(Font, FontStyle.Bold);
        using var small = new Font("Segoe UI", 8f);
        using var brFg = new SolidBrush(Fg);
        using var brDim = new SolidBrush(Dim);

        int contentW = W - Pad * 2;

        if (_snapshots.Count == 0)
        {
            g.DrawString("Belum ada AI yang login.", bold, brFg, Pad, Pad);
            g.DrawString("Klik kanan ikon tray → Login.", small, brDim, Pad, Pad + 22);
            return;
        }

        int y = Pad;

        foreach (var s in _snapshots)
        {
            // Header: provider name (left) + plan/account (right)
            g.DrawString(s.DisplayName, bold, brFg, Pad, y);
            var rightLabel = s.Plan ?? s.AccountLabel;
            if (!string.IsNullOrEmpty(rightLabel))
                DrawRight(g, rightLabel!, small, brDim, Pad, y + 2, contentW);
            y += 26;

            if (!s.Ok)
            {
                using var brErr = new SolidBrush(Color.FromArgb(235, 120, 120));
                var rect = new RectangleF(Pad, y, contentW, 30);
                g.DrawString(s.Error, small, brErr, rect);
                y += 34;
            }
            else if (s.Windows.Count == 0)
            {
                g.DrawString("No data", small, brDim, Pad, y);
                y += 20;
            }
            else
            {
                foreach (var w in s.Windows)
                {
                    // Row line 1: label (left) + detail/countdown (right)
                    g.DrawString(w.Label, Font, brFg, Pad, y);

                    var right = BuildRight(w);
                    if (!string.IsNullOrEmpty(right))
                        DrawRight(g, right, Font, brDim, Pad, y, contentW);
                    y += 19;

                    // Row line 2: bar
                    var barRect = new Rectangle(Pad, y, contentW, 6);
                    using (var trackBrush = new SolidBrush(Track))
                        FillRounded(g, trackBrush, barRect, 3);

                    if (w.Utilization is double u && u >= 0)
                    {
                        int fillW = (int)Math.Round(contentW * Math.Clamp(u, 0, 1));
                        if (fillW > 2)
                        {
                            using var fill = new SolidBrush(BarColor(u));
                            FillRounded(g, fill, new Rectangle(Pad, y, fillW, 6), 3);
                        }
                    }
                    y += 17;
                }
            }

            y += 6;
            using var sep = new Pen(Color.FromArgb(44, 44, 50));
            g.DrawLine(sep, Pad, y, W - Pad, y);
            y += 6;
        }
    }

    private static string BuildRight(UsageWindow w)
    {
        var parts = new List<string>();
        if (w.Utilization is double u && u >= 0) parts.Add($"{u * 100:0}%");
        if (!string.IsNullOrEmpty(w.Detail)) parts.Add(w.Detail!);
        var reset = FormatReset(w.ResetsAt);
        if (reset is not null) parts.Add(reset);
        return string.Join("  ·  ", parts);
    }

    private static string? FormatReset(DateTimeOffset? at)
    {
        if (at is null) return null;
        var span = at.Value - DateTimeOffset.Now;
        if (span <= TimeSpan.Zero) return "resetting";
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{span.Minutes}m";
    }

    private static Color BarColor(double u) => u switch
    {
        >= 0.9 => Color.FromArgb(235, 87, 87),
        >= 0.7 => Color.FromArgb(242, 178, 70),
        _ => Color.FromArgb(120, 200, 140),
    };

    private static void DrawRight(Graphics g, string text, Font font, Brush brush, int pad, int y, int contentW)
    {
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, pad + contentW - size.Width, y);
    }

    private static void FillRounded(Graphics g, Brush brush, Rectangle r, int radius)
    {
        if (r.Width <= 0) return;
        radius = Math.Min(radius, r.Height / 2);
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
