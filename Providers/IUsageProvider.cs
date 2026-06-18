namespace WinCodexBar;

public interface IUsageProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>True when this provider has usable credentials (so it should be shown/fetched).</summary>
    bool IsLoggedIn(ProviderConfig cfg);

    Task<ProviderSnapshot> FetchAsync(ProviderConfig cfg, CancellationToken ct);
}

internal static class Net
{
    public static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var h = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("WinCodexBar/0.2");
        return h;
    }
}
