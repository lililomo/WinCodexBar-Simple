using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinCodexBar;

/// <summary>OAuth tokens obtained via a browser login (PKCE). Stored per provider.</summary>
public sealed class OAuthTokens
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? IdToken { get; set; }

    /// <summary>Provider-specific account/subscription id parsed from the id_token, when available.</summary>
    public string? AccountId { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(AccessToken);
}

public sealed class ProviderConfig
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>API key. Claude: sk-ant-admin... | OpenAI: sk-admin... (or sk-... legacy) | Copilot: leave null, use Token.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Claude only: the claude.ai sessionKey cookie value (starts with sk-ant-sid01-...).</summary>
    public string? SessionKey { get; set; }

    /// <summary>Copilot only: a GitHub token with Copilot access (or set the COPILOT_API_TOKEN env var instead).</summary>
    public string? Token { get; set; }

    /// <summary>Tokens from a browser OAuth login (Claude Pro/Max, ChatGPT/Codex).</summary>
    public OAuthTokens? OAuth { get; set; }
}

/// <summary>How the usage panel is positioned.</summary>
public enum PopupMode
{
    /// <summary>Pops up anchored at the tray corner each time, hides when it loses focus.</summary>
    AnchoredToTray,

    /// <summary>Free-floating window the user can drag anywhere; its position is remembered.</summary>
    Floating,
}

/// <summary>What the panel's close (×) button does.</summary>
public enum CloseAction
{
    /// <summary>Just hide the panel window (the tray app keeps running).</summary>
    HideWindow,

    /// <summary>Quit the whole application.</summary>
    QuitApp,
}

/// <summary>Look &amp; feel of the popup panel (set from the Settings dialog).</summary>
public sealed class UiConfig
{
    /// <summary>Panel opacity, 20..100 (%).</summary>
    public int Opacity { get; set; } = 100;

    public bool AlwaysOnTop { get; set; } = true;

    public PopupMode PopupMode { get; set; } = PopupMode.AnchoredToTray;

    /// <summary>Saved position for Floating mode. -1 = not set yet.</summary>
    public int FloatingX { get; set; } = -1;
    public int FloatingY { get; set; } = -1;

    /// <summary>Colour theme name (see <see cref="WinCodexBar.UI.Theme"/>).</summary>
    public string Theme { get; set; } = "Midnight";

    /// <summary>What the panel's × button does.</summary>
    public CloseAction CloseButton { get; set; } = CloseAction.HideWindow;

    /// <summary>Display order of provider cards (by id). Reordered by drag-and-drop.</summary>
    public List<string> ProviderOrder { get; set; } = new();
}

public sealed class AppConfig
{
    public int RefreshSeconds { get; set; } = 300;
    public UiConfig Ui { get; set; } = new();
    public List<ProviderConfig> Providers { get; set; } = new();

    [JsonIgnore]
    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCodexBar");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProviderConfig For(string id)
    {
        var p = Providers.FirstOrDefault(x => x.Id == id);
        if (p is null)
        {
            p = new ProviderConfig { Id = id };
            Providers.Add(p);
        }
        return p;
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new AppConfig();
        }
        catch
        {
            // Corrupt config: fall back to defaults rather than crashing on startup.
        }
        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
    }
}
