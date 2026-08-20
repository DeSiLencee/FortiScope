using FortiScope.Data;
using FortiScope.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FortiScope.Services;

public sealed record SystemHistoryPoint(DateTime TimestampUtc, double? CpuUsage,
    double? MemoryUsage, double? SessionCount, bool Connected);

public sealed record InterfaceHistoryPoint(DateTime TimestampUtc, double IncomingMbps,
    double OutgoingMbps, double TotalMbps, double MaxTotalMbps, double? UtilizationPercent);

public sealed class HistoryService(IDbContextFactory<FortiScopeDbContext> dbContextFactory,
    IOptions<SnmpOptions> snmpOptions)
{
    private const int MaxPoints = 500;

    public async Task<IReadOnlyList<SystemHistoryPoint>> GetSystemHistoryAsync(TimeSpan range,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.Subtract(range);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var samples = await dbContext.DeviceMetricSamples.AsNoTracking()
            .Where(item => item.DeviceIp == snmpOptions.Value.Host && item.TimestampUtc >= cutoff)
            .OrderBy(item => item.TimestampUtc)
            .Select(item => new SystemHistoryPoint(item.TimestampUtc, item.CpuUsage, item.MemoryUsage,
                item.SessionCount, item.Connected))
            .ToListAsync(cancellationToken);
        return DownsampleSystem(samples);
    }

    public async Task<IReadOnlyList<InterfaceHistoryPoint>> GetInterfaceHistoryAsync(int interfaceIndex,
        TimeSpan range, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.Subtract(range);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var samples = await dbContext.InterfaceMetricSamples.AsNoTracking()
            .Where(item => item.DeviceIp == snmpOptions.Value.Host &&
                           item.InterfaceIndex == interfaceIndex && item.TimestampUtc >= cutoff)
            .OrderBy(item => item.TimestampUtc)
            .Select(item => new InterfaceHistoryPoint(item.TimestampUtc, item.IncomingMbps,
                item.OutgoingMbps, item.TotalMbps, item.TotalMbps, item.UtilizationPercent))
            .ToListAsync(cancellationToken);
        return DownsampleInterfaces(samples);
    }

    private static IReadOnlyList<SystemHistoryPoint> DownsampleSystem(IReadOnlyList<SystemHistoryPoint> samples)
    {
        var size = GetBucketSize(samples.Count);
        if (size == 1) return samples;
        return samples.Chunk(size).Select(bucket => new SystemHistoryPoint(
            bucket[0].TimestampUtc, AverageNullable(bucket.Select(item => item.CpuUsage)),
            AverageNullable(bucket.Select(item => item.MemoryUsage)),
            AverageNullable(bucket.Select(item => item.SessionCount)), bucket.Any(item => item.Connected))).ToArray();
    }

    private static IReadOnlyList<InterfaceHistoryPoint> DownsampleInterfaces(IReadOnlyList<InterfaceHistoryPoint> samples)
    {
        var size = GetBucketSize(samples.Count);
        if (size == 1) return samples;
        return samples.Chunk(size).Select(bucket => new InterfaceHistoryPoint(
            bucket[0].TimestampUtc, bucket.Average(item => item.IncomingMbps),
            bucket.Average(item => item.OutgoingMbps), bucket.Average(item => item.TotalMbps),
            bucket.Max(item => item.TotalMbps), AverageNullable(bucket.Select(item => item.UtilizationPercent)))).ToArray();
    }

    private static int GetBucketSize(int count) => Math.Max(1, (int)Math.Ceiling(count / (double)MaxPoints));

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var available = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return available.Length == 0 ? null : available.Average();
    }
}
