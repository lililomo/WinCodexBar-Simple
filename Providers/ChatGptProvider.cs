using WinCodexBar.Auth;

namespace WinCodexBar.Providers;

/// <summary>
/// ChatGPT / Codex SUBSCRIPTION (signed in with ChatGPT) — not the OpenAI API.
///
/// We can reliably show identity/plan from the OAuth id_token. There is currently
/// NO stable, documented endpoint that exposes the ChatGPT/Codex usage windows
/// (limits are surfaced by Codex only as response headers during a request), so this
/// card shows login status + plan rather than a usage bar. // VERIFY if a usage API appears.
/// </summary>
public sealed class ChatGptProvider : IUsageProvider
{
    public string Id => "chatgpt";
    public string DisplayName => "ChatGPT / Codex";

    public bool IsLoggedIn(ProviderConfig cfg) => cfg.OAuth?.IsValid ?? false;

    public Task<ProviderSnapshot> FetchAsync(ProviderConfig cfg, CancellationToken ct)
    {
        var snap = new ProviderSnapshot { ProviderId = Id, DisplayName = DisplayName };

        if (cfg.OAuth?.IsValid != true)
        {
            snap.Error = "Belum login. Klik kanan ikon → Login → ChatGPT / Codex.";
            return Task.FromResult(snap);
        }

        var (account, plan) = ChatGptAuth.ReadIdToken(cfg.OAuth.IdToken);
        snap.Plan = string.IsNullOrWhiteSpace(plan) ? "ChatGPT" : plan;
        snap.AccountLabel = account;

        snap.Windows.Add(new UsageWindow { Label = "Status", Detail = "Logged in" });
        if (cfg.OAuth.ExpiresAt is { } exp)
            snap.Windows.Add(new UsageWindow { Label = "Sesi berakhir", ResetsAt = exp });

        return Task.FromResult(snap);
    }
}
