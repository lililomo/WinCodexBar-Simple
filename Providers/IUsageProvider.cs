namespace WinCodexBar;

public interface IUsageProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>True when this provider has usable credentials (so it should be shown/fetched).</summary>
    bool IsLoggedIn(ProviderConfig cfg);

    Task<ProviderSnapshot> FetchAsync(ProviderConfig cfg, CancellationToken ct);
}

/// <summary>Thrown when a provider endpoint returns HTTP 429. Carries the server's Retry-After if given.</summary>
public sealed class RateLimitException : Exception
{
    public TimeSpan? RetryAfter { get; }
    public RateLimitException(TimeSpan? retryAfter) : base("rate limited (429)") => RetryAfter = retryAfter;
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

    /// <summary>The Retry-After delay from a response, if present.</summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage res)
    {
        var ra = res.Headers.RetryAfter;
        if (ra is null) return null;
        if (ra.Delta is TimeSpan d) return d;
        if (ra.Date is DateTimeOffset dt)
        {
            var s = dt - DateTimeOffset.UtcNow;
            return s > TimeSpan.Zero ? s : null;
        }
        return null;
    }

    /// <summary>Like EnsureSuccessStatusCode, but surfaces 429 as <see cref="RateLimitException"/>.</summary>
    public static void EnsureOk(HttpResponseMessage res)
    {
        if ((int)res.StatusCode == 429) throw new RateLimitException(RetryAfter(res));
        res.EnsureSuccessStatusCode();
    }
}
