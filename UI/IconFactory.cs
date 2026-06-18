using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WinCodexBar.UI;

internal static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Two little bars. peak: highest utilization 0..1 (null = unknown) to tint the accent.</summary>
    public static Icon Build(double? peak)
    {
        const int s = 32;
        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var accent = peak switch
            {
                null => Color.FromArgb(150, 150, 160),
                >= 0.9 => Color.FromArgb(235, 87, 87),
                >= 0.7 => Color.FromArgb(242, 178, 70),
                _ => Color.FromArgb(120, 200, 140),
            };

            using var dim = new SolidBrush(Color.FromArgb(120, 120, 130));
            using var hot = new SolidBrush(accent);

            // Bar 1 (short), Bar 2 (tall)
            g.FillRectangle(dim, 6, 16, 7, 12);
            g.FillRectangle(hot, 18, 8, 7, 20);
        }

        var hicon = bmp.GetHicon();
        try
        {
            // Clone so we can free the GDI handle immediately and avoid a leak.
            using var tmp = Icon.FromHandle(hicon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }
}
