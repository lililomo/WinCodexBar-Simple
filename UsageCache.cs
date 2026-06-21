using System.Text.Json;

namespace WinCodexBar;

/// <summary>
/// Persists the last successful usage snapshot per provider to disk, so the panel can keep
/// showing the most recent numbers across restarts and while a provider is rate-limited.
/// </summary>
internal static class UsageCache
{
    private static string FilePath => Path.Combine(AppConfig.Dir, "cache.json");

    private sealed class W
    {
        public string Label { get; set; } = "";
        public double? Utilization { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
        public string? Detail { get; set; }
    }

    private sealed class P
    {
        public string ProviderId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? Plan { get; set; }
        public string? AccountLabel { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public List<W> Windows { get; set; } = new();
    }

    public static Dictionary<string, ProviderSnapshot> Load()
    {
        var dict = new Dictionary<string, ProviderSnapshot>();
        try
        {
            if (!File.Exists(FilePath)) return dict;
            var data = JsonSerializer.Deserialize<List<P>>(File.ReadAllText(FilePath)) ?? new();
            foreach (var p in data)
            {
                var snap = new ProviderSnapshot
                {
                    ProviderId = p.ProviderId,
                    DisplayName = p.DisplayName,
                    Plan = p.Plan,
                    AccountLabel = p.AccountLabel,
                    FetchedAt = p.FetchedAt,
                };
                foreach (var w in p.Windows)
                    snap.Windows.Add(new UsageWindow { Label = w.Label, Utilization = w.Utilization, ResetsAt = w.ResetsAt, Detail = w.Detail });
                dict[p.ProviderId] = snap;
            }
        }
        catch
        {
            // ignore a corrupt cache
        }
        return dict;
    }

    public static void Save(IReadOnlyDictionary<string, ProviderSnapshot> cache)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            var data = cache.Values
                .Where(s => s.Ok && s.Windows.Count > 0)
                .Select(s => new P
                {
                    ProviderId = s.ProviderId,
                    DisplayName = s.DisplayName,
                    Plan = s.Plan,
                    AccountLabel = s.AccountLabel,
                    FetchedAt = s.FetchedAt,
                    Windows = s.Windows.Select(w => new W
                    {
                        Label = w.Label,
                        Utilization = w.Utilization,
                        ResetsAt = w.ResetsAt,
                        Detail = w.Detail,
                    }).ToList(),
                })
                .ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // never let caching crash a refresh
        }
    }
}
