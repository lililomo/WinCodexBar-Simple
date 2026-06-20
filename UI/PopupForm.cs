using System.Drawing;
using System.Drawing.Drawing2D;

namespace WinCodexBar.UI;

internal sealed class PopupForm : Form
{
    private List<ProviderSnapshot> _snapshots = new();
    private List<ProviderSnapshot> _ordered = new();

    private AppConfig? _cfg;
    private Action? _save;
    private Func<Task>? _refresh;
    private Action? _quit;

    private Theme _theme = Theme.Midnight;

    private readonly Label _refreshBtn = new();
    private readonly Label _closeBtn = new();

    // window drag (floating mode)
    private bool _draggingWindow;
    private Point _windowDragOffset;

    // card reorder drag
    private readonly List<(string id, Rectangle rect)> _cards = new();
    private string? _grabId;
    private bool _reordering;
    private int _grabStartY;
    private Point _cursor;

    private const int W = 340;
    private const int OuterPad = 14;
    private const int HeaderH = 46;
    private const int HeaderGap = 12;   // breathing room between header and first card
    private const int CardGap = 10;
    private const int CardPad = 14;
    private const int RowH = 18;
    private const int BarH = 7;
    private const int RowGap = 9;

    public PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = _theme.Bg;
        Width = W;
        Height = 220;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);

        foreach (var (lbl, glyph) in new[] { (_refreshBtn, "↻"), (_closeBtn, "✕") })
        {
            lbl.Text = glyph;
            lbl.Font = new Font("Segoe UI", glyph == "✕" ? 11f : 12f, FontStyle.Regular);
            lbl.AutoSize = false;
            lbl.Size = new Size(28, 28);
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.Cursor = Cursors.Hand;
            Controls.Add(lbl);
        }

        _refreshBtn.MouseEnter += (_, _) => _refreshBtn.ForeColor = _theme.Fg;
        _refreshBtn.MouseLeave += (_, _) => _refreshBtn.ForeColor = _theme.Dim;
        _refreshBtn.Click += async (_, _) => { if (_refresh is not null) { try { await _refresh(); } catch { } } };

        _closeBtn.MouseEnter += (_, _) => _closeBtn.ForeColor = _theme.Crit;
        _closeBtn.MouseLeave += (_, _) => _closeBtn.ForeColor = _theme.Dim;
        _closeBtn.Click += (_, _) =>
        {
            if (_cfg?.Ui.CloseButton == CloseAction.QuitApp) _quit?.Invoke();
            else Hide();
        };
    }

    public void Configure(AppConfig cfg, Action save, Func<Task> refresh, Action quit)
    {
        _cfg = cfg;
        _save = save;
        _refresh = refresh;
        _quit = quit;
    }

    public void ApplyUi()
    {
        if (_cfg is null) return;
        _theme = Theme.Get(_cfg.Ui.Theme);
        Opacity = Math.Clamp(_cfg.Ui.Opacity, 20, 100) / 100.0;
        TopMost = _cfg.Ui.AlwaysOnTop;
        BackColor = _theme.Bg;

        foreach (var b in new[] { _refreshBtn, _closeBtn })
        {
            b.BackColor = _theme.Header;
            b.ForeColor = _theme.Dim;
        }
        LayoutButtons();
        Invalidate();
    }

    private void LayoutButtons()
    {
        int y = (HeaderH - 28) / 2;
        _closeBtn.Location = new Point(W - OuterPad - 28, y);
        _refreshBtn.Location = new Point(W - OuterPad - 28 - 28 - 2, y);
    }

    private bool Floating => _cfg?.Ui.PopupMode == PopupMode.Floating;

    public void Render(List<ProviderSnapshot> snapshots)
    {
        _snapshots = snapshots;
        _ordered = OrderedForDisplay();
        Height = Measure();
        ApplyRegion();
        Reposition();
        LayoutButtons();
        Invalidate();
    }

    private List<ProviderSnapshot> OrderedForDisplay()
    {
        var order = _cfg?.Ui.ProviderOrder ?? new List<string>();
        return _snapshots
            .Where(s => _cfg is null || _cfg.For(s.ProviderId).Enabled)   // hidden providers are dropped
            .OrderBy(s => { int i = order.IndexOf(s.ProviderId); return i < 0 ? int.MaxValue : i; })
            .ToList();
    }

    private void ApplyRegion()
    {
        using var path = Rounded(new Rectangle(0, 0, Width, Height), 14);
        Region = new Region(path);
    }

    private void Reposition()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);

        if (Floating && _cfg is { Ui.FloatingX: >= 0, Ui.FloatingY: >= 0 })
        {
            var saved = new Point(_cfg.Ui.FloatingX, _cfg.Ui.FloatingY);
            var area = Screen.FromPoint(saved).WorkingArea;
            Left = Math.Clamp(_cfg.Ui.FloatingX, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Clamp(_cfg.Ui.FloatingY, area.Top, Math.Max(area.Top, area.Bottom - Height));
            return;
        }

        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!Floating && !_reordering) Hide();
    }

    // ---- sizing -------------------------------------------------------------

    private int CardHeight(ProviderSnapshot s)
    {
        int h = CardPad + 24; // top pad + provider header row
        if (!s.Ok) h += 30;
        else if (s.Windows.Count == 0) h += 18;
        else
        {
            foreach (var w in s.Windows) h += RowH + (w.Utilization is not null ? BarH + 4 : 0) + RowGap;
            h -= RowGap;
        }
        return h + CardPad;
    }

    private int Measure()
    {
        if (_ordered.Count == 0) return HeaderH + 78;
        int h = HeaderH + HeaderGap;
        foreach (var s in _ordered) h += CardHeight(s) + CardGap;
        return h + (OuterPad - CardGap) + OuterPad;
    }

    // ---- painting -----------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // window background + hairline
        using (var bg = new SolidBrush(_theme.Bg)) using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 14))
        {
            g.FillPath(bg, path);
            using var border = new Pen(_theme.Border);
            g.DrawPath(border, path);
        }

        DrawHeader(g);

        _cards.Clear();
        if (_ordered.Count == 0)
        {
            using var dimBig = new Font("Segoe UI", 10f, FontStyle.Bold);
            using var brFg = new SolidBrush(_theme.Fg);
            using var brDim = new SolidBrush(_theme.Dim);
            using var small = new Font("Segoe UI", 8.5f);
            g.DrawString("No providers connected", dimBig, brFg, OuterPad, HeaderH + 8);
            g.DrawString("Right-click the tray icon → Login.", small, brDim, OuterPad, HeaderH + 30);
            return;
        }

        int cardW = W - OuterPad * 2;
        int y = HeaderH + HeaderGap;
        int? insertLineY = null;
        int dropIndex = _reordering ? DropIndex() : -1;

        for (int i = 0; i < _ordered.Count; i++)
        {
            var s = _ordered[i];
            int ch = CardHeight(s);
            var rect = new Rectangle(OuterPad, y, cardW, ch);
            _cards.Add((s.ProviderId, rect));

            if (_reordering && dropIndex == i) insertLineY = y - CardGap / 2;

            bool isGrabbed = _reordering && s.ProviderId == _grabId;
            if (!isGrabbed) DrawCard(g, s, rect, false);
            else
            {
                using var ghost = new SolidBrush(Color.FromArgb(40, _theme.Accent));
                using var p = Rounded(rect, 12);
                g.FillPath(ghost, p); // leave a faint placeholder where it was
            }
            y += ch + CardGap;
        }
        if (_reordering && dropIndex >= _ordered.Count) insertLineY = y - CardGap / 2;

        if (insertLineY is int ly)
        {
            using var accent = new Pen(_theme.Accent, 2f);
            g.DrawLine(accent, OuterPad + 2, ly, W - OuterPad - 2, ly);
        }

        // floating copy of the grabbed card following the cursor
        if (_reordering && _grabId is not null)
        {
            var grabbed = _ordered.FirstOrDefault(s => s.ProviderId == _grabId);
            if (grabbed is not null)
            {
                int ch = CardHeight(grabbed);
                var rect = new Rectangle(OuterPad, _cursor.Y - ch / 2, cardW, ch);
                using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                using (var sp = Rounded(new Rectangle(rect.X + 2, rect.Y + 3, rect.Width, rect.Height), 12))
                    g.FillPath(shadow, sp);
                DrawCard(g, grabbed, rect, true);
            }
        }
    }

    private void DrawHeader(Graphics g)
    {
        using var headerBg = new SolidBrush(_theme.Header);
        using var top = Rounded(new Rectangle(0, 0, Width - 1, HeaderH + 14), 14);
        g.SetClip(new Rectangle(0, 0, Width, HeaderH));
        g.FillPath(headerBg, top);
        g.ResetClip();

        using var line = new Pen(_theme.Border);
        g.DrawLine(line, 0, HeaderH, Width, HeaderH);

        // accent square + title
        using (var acc = new SolidBrush(_theme.Accent)) using (var ap = Rounded(new Rectangle(OuterPad, HeaderH / 2 - 7, 14, 14), 4))
            g.FillPath(acc, ap);
        using var title = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        using var brFg = new SolidBrush(_theme.Fg);
        g.DrawString("WinCodexBar", title, brFg, OuterPad + 22, HeaderH / 2 - 11);
    }

    private void DrawCard(Graphics g, ProviderSnapshot s, Rectangle r, bool lifted)
    {
        using (var cardBrush = new SolidBrush(lifted ? _theme.CardHover : _theme.Card))
        using (var cp = Rounded(r, 12))
        {
            g.FillPath(cardBrush, cp);
            using var border = new Pen(_theme.Border);
            g.DrawPath(border, cp);
        }

        using var bold = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        var regular = Font; // shared form font — do NOT dispose
        using var small = new Font("Segoe UI", 8.5f);
        using var brFg = new SolidBrush(_theme.Fg);
        using var brDim = new SolidBrush(_theme.Dim);

        int x = r.X + CardPad;
        int right = r.Right - CardPad;
        int y = r.Y + CardPad;

        // grip handle (drag affordance)
        using (var grip = new SolidBrush(_theme.Dim))
            for (int gx = 0; gx < 2; gx++)
                for (int gy = 0; gy < 3; gy++)
                    g.FillEllipse(grip, x + gx * 4, y + 2 + gy * 5, 2.4f, 2.4f);

        // provider colour dot + name
        using (var dot = new SolidBrush(_theme.ProviderAccent(s.ProviderId)))
            g.FillEllipse(dot, x + 14, y + 3, 9, 9);
        g.DrawString(s.DisplayName, bold, brFg, x + 28, y - 1);

        // plan / account pill on the right
        var pill = s.Plan ?? s.AccountLabel;
        if (!string.IsNullOrEmpty(pill)) DrawPill(g, pill!, right, y, small);

        y += 24;

        if (!s.Ok)
        {
            using var brErr = new SolidBrush(_theme.Crit);
            g.DrawString(s.Error, small, brErr, new RectangleF(x, y, right - x, 28));
            return;
        }
        if (s.Windows.Count == 0)
        {
            g.DrawString("No data", small, brDim, x, y);
            return;
        }

        foreach (var w in s.Windows)
        {
            g.DrawString(w.Label, regular, brFg, x, y);
            var rt = BuildRight(w);
            if (!string.IsNullOrEmpty(rt)) DrawRight(g, rt, regular, brDim, x, right, y);
            y += RowH;

            if (w.Utilization is double u && u >= 0)
            {
                var track = new Rectangle(x, y, right - x, BarH);
                using (var tb = new SolidBrush(_theme.Track)) FillRounded(g, tb, track, BarH / 2);
                int fillW = (int)Math.Round((right - x) * Math.Clamp(u, 0, 1));
                if (fillW > 2)
                    using (var fb = new SolidBrush(_theme.BarColor(u)))
                        FillRounded(g, fb, new Rectangle(x, y, fillW, BarH), BarH / 2);
                y += BarH + 4;
            }
            y += RowGap;
        }
    }

    private void DrawPill(Graphics g, string text, int rightX, int y, Font font)
    {
        var sz = g.MeasureString(text, font);
        int w = (int)sz.Width + 16, h = 18;
        var rect = new Rectangle(rightX - w, y, w, h);
        using (var bg = new SolidBrush(_theme.Track)) FillRounded(g, bg, rect, 9);
        using var br = new SolidBrush(_theme.Dim);
        g.DrawString(text, font, br, rect.X + 8, rect.Y + 2);
    }

    private string BuildRight(UsageWindow w)
    {
        var parts = new List<string>();
        if (w.Utilization is double u && u >= 0) parts.Add($"{u * 100:0}%");
        if (!string.IsNullOrEmpty(w.Detail)) parts.Add(w.Detail!);
        var reset = FormatReset(w.ResetsAt);
        if (reset is not null) parts.Add(reset);
        return string.Join("   ·   ", parts);
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

    private static void DrawRight(Graphics g, string text, Font font, Brush brush, int x, int rightX, int y)
    {
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, rightX - size.Width, y);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) { path.AddRectangle(r); return path; }
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void FillRounded(Graphics g, Brush brush, Rectangle r, int radius)
    {
        if (r.Width <= 0) return;
        using var path = Rounded(r, radius);
        g.FillPath(brush, path);
    }

    // ---- mouse: window drag (header) + card reorder -------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (e.Y < HeaderH)
        {
            if (Floating) { _draggingWindow = true; _windowDragOffset = e.Location; }
            return;
        }

        foreach (var c in _cards)
            if (c.rect.Contains(e.Location)) { _grabId = c.id; _grabStartY = e.Y; _cursor = e.Location; return; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingWindow)
        {
            Left += e.X - _windowDragOffset.X;
            Top += e.Y - _windowDragOffset.Y;
            return;
        }
        if (_grabId is not null)
        {
            _cursor = e.Location;
            if (!_reordering && Math.Abs(e.Y - _grabStartY) > 6) _reordering = true;
            if (_reordering) Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_draggingWindow)
        {
            _draggingWindow = false;
            if (_cfg is not null) { _cfg.Ui.FloatingX = Left; _cfg.Ui.FloatingY = Top; _save?.Invoke(); }
            return;
        }
        if (_grabId is not null)
        {
            if (_reordering) CommitReorder(_grabId, DropIndex());
            _grabId = null;
            _reordering = false;
            Invalidate();
        }
    }

    /// <summary>Insertion index (0..count) among displayed cards, from the cursor Y.</summary>
    private int DropIndex()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            var r = _cards[i].rect;
            if (_cursor.Y < r.Y + r.Height / 2) return i;
        }
        return _cards.Count;
    }

    private void CommitReorder(string id, int insertAt)
    {
        if (_cfg is null) return;

        var displayed = _ordered.Select(s => s.ProviderId).ToList();
        int from = displayed.IndexOf(id);
        if (from < 0) return;

        if (insertAt > from) insertAt--;            // account for removal shift
        insertAt = Math.Clamp(insertAt, 0, displayed.Count - 1);
        if (insertAt == from) return;

        displayed.RemoveAt(from);
        displayed.Insert(insertAt, id);

        // Rewrite the full ProviderOrder, substituting displayed ids into their slots
        // while keeping non-displayed ids in place.
        var shown = new HashSet<string>(displayed);
        var queue = new Queue<string>(displayed);
        var result = new List<string>();
        foreach (var pid in _cfg.Ui.ProviderOrder)
            result.Add(shown.Contains(pid) ? queue.Dequeue() : pid);
        while (queue.Count > 0) result.Add(queue.Dequeue());

        _cfg.Ui.ProviderOrder = result;
        _save?.Invoke();
        Render(_snapshots);
    }
}
