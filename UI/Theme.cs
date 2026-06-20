using System.Drawing;

namespace WinCodexBar.UI;

/// <summary>A colour palette for the panel. Five built-in themes are provided.</summary>
internal sealed class Theme
{
    public string Name { get; init; } = "";
    public Color Bg { get; init; }        // window background
    public Color Header { get; init; }    // header strip
    public Color Card { get; init; }      // provider card surface
    public Color CardHover { get; init; } // card while dragging
    public Color Fg { get; init; }        // primary text
    public Color Dim { get; init; }       // secondary text
    public Color Track { get; init; }     // bar track / pill background
    public Color Border { get; init; }    // hairline borders
    public Color Accent { get; init; }    // brand accent (title, insertion line)

    // utilization severity colours
    public Color Good { get; init; }
    public Color Warn { get; init; }
    public Color Crit { get; init; }

    public bool IsDark { get; init; } = true;

    public static readonly string[] Names = { "Midnight", "Slate", "Indigo", "Forest", "Light" };

    public static Theme Get(string? name) => name switch
    {
        "Slate" => Slate,
        "Indigo" => Indigo,
        "Forest" => Forest,
        "Light" => Light,
        _ => Midnight,
    };

    private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    public Color BarColor(double u) => u >= 0.9 ? Crit : u >= 0.7 ? Warn : Good;

    /// <summary>A brand-ish accent colour per provider, used for the card's left marker.</summary>
    public Color ProviderAccent(string id) => id switch
    {
        "claude" => C(214, 122, 87),    // terracotta
        "chatgpt" => C(16, 163, 127),   // teal-green
        "copilot" => C(88, 166, 255),   // github blue
        "deepseek" => C(99, 102, 241),  // indigo
        _ => Accent,
    };

    public static readonly Theme Midnight = new()
    {
        Name = "Midnight",
        Bg = C(20, 22, 27), Header = C(24, 27, 33), Card = C(30, 33, 41), CardHover = C(38, 42, 52),
        Fg = C(233, 235, 240), Dim = C(150, 156, 167), Track = C(45, 50, 60), Border = C(40, 44, 53),
        Accent = C(112, 140, 255), Good = C(86, 196, 130), Warn = C(240, 178, 74), Crit = C(238, 96, 96),
    };

    public static readonly Theme Slate = new()
    {
        Name = "Slate",
        Bg = C(27, 30, 35), Header = C(32, 35, 41), Card = C(38, 42, 49), CardHover = C(47, 52, 60),
        Fg = C(228, 231, 235), Dim = C(155, 162, 173), Track = C(52, 57, 66), Border = C(48, 53, 61),
        Accent = C(124, 142, 170), Good = C(94, 198, 138), Warn = C(238, 180, 86), Crit = C(236, 102, 102),
    };

    public static readonly Theme Indigo = new()
    {
        Name = "Indigo",
        Bg = C(19, 22, 38), Header = C(23, 27, 46), Card = C(28, 33, 56), CardHover = C(36, 42, 72),
        Fg = C(228, 231, 245), Dim = C(150, 157, 192), Track = C(42, 48, 80), Border = C(38, 44, 73),
        Accent = C(129, 140, 248), Good = C(74, 222, 160), Warn = C(245, 184, 84), Crit = C(244, 102, 116),
    };

    public static readonly Theme Forest = new()
    {
        Name = "Forest",
        Bg = C(20, 26, 24), Header = C(23, 31, 28), Card = C(29, 38, 34), CardHover = C(37, 48, 43),
        Fg = C(229, 236, 231), Dim = C(150, 165, 156), Track = C(42, 54, 48), Border = C(38, 49, 44),
        Accent = C(79, 178, 134), Good = C(96, 200, 142), Warn = C(238, 182, 86), Crit = C(235, 104, 104),
    };

    public static readonly Theme Light = new()
    {
        Name = "Light", IsDark = false,
        Bg = C(243, 244, 247), Header = C(249, 250, 252), Card = C(255, 255, 255), CardHover = C(240, 243, 248),
        Fg = C(28, 33, 41), Dim = C(108, 116, 128), Track = C(228, 231, 236), Border = C(224, 227, 232),
        Accent = C(37, 99, 235), Good = C(34, 160, 100), Warn = C(202, 138, 30), Crit = C(214, 64, 76),
    };
}
