namespace FortiScope.Services;

public static class HistoryRangeParser
{
    private static readonly IReadOnlyDictionary<string, TimeSpan> Ranges = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
    {
        ["5m"] = TimeSpan.FromMinutes(5), ["1h"] = TimeSpan.FromHours(1),
        ["6h"] = TimeSpan.FromHours(6), ["24h"] = TimeSpan.FromHours(24),
        ["7d"] = TimeSpan.FromDays(7), ["30d"] = TimeSpan.FromDays(30)
    };

    public static bool TryParse(string? value, out TimeSpan range) =>
        Ranges.TryGetValue(value ?? string.Empty, out range);
}
