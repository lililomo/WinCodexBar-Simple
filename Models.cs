namespace WinCodexBar;

/// <summary>One quota/usage window for a provider (e.g. a 5h session window, a weekly window, or a spend bucket).</summary>
public sealed class UsageWindow
{
    public string Label { get; init; } = "";

    /// <summary>0.0 .. 1.0 fraction used. Null when the provider only reports spend/credits and has no bar.</summary>
    public double? Utilization { get; init; }

    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>Free-form right-aligned detail, e.g. "$12.40" or "320 / 1000".</summary>
    public string? Detail { get; init; }
}

/// <summary>The full result of fetching one provider.</summary>
public sealed class ProviderSnapshot
{
    public string ProviderId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? AccountLabel { get; set; }
    public string? Plan { get; set; }
    public List<UsageWindow> Windows { get; } = new();
    public string? Error { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.Now;

    public bool Ok => Error is null;

    /// <summary>Highest utilization across windows, used to colour the tray icon. Null if nothing measurable.</summary>
    public double? PeakUtilization
    {
        get
        {
            var values = Windows
                .Where(w => w.Utilization is not null)
                .Select(w => w.Utilization!.Value)
                .ToList();
            return values.Count == 0 ? null : values.Max();
        }
    }
}
