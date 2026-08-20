using FortiScope.Configuration;
using FortiScope.Data;
using FortiScope.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FortiScope.Services;

public sealed class MetricPersistenceBackgroundService(
    ISnmpMonitoringService monitoringService,
    IDbContextFactory<FortiScopeDbContext> dbContextFactory,
    IOptions<MonitoringOptions> options,
    ILogger<MetricPersistenceBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PersistenceIntervalSeconds));
    private readonly int _retentionDays = Math.Max(1, options.Value.RetentionDays);
    private string? _lastSnapshotKey;
    private DateTimeOffset _nextRetentionUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            await PersistSafelyAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PersistSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var snapshot = monitoringService.GetCurrent();
            var key = MetricPersistencePolicy.GetSnapshotKey(snapshot);
            if (MetricPersistencePolicy.ShouldPersist(key, _lastSnapshotKey))
            {
                var deviceSample = MetricPersistencePolicy.CreateDeviceSample(snapshot, DateTimeOffset.UtcNow);
                var alreadyExists = await dbContext.DeviceMetricSamples.AsNoTracking().AnyAsync(item =>
                    item.DeviceIp == deviceSample.DeviceIp && item.TimestampUtc == deviceSample.TimestampUtc,
                    cancellationToken);

                if (!alreadyExists)
                {
                    dbContext.DeviceMetricSamples.Add(deviceSample);
                    if (snapshot.Connected)
                    {
                        dbContext.InterfaceMetricSamples.AddRange(snapshot.Interfaces.Select(item => new InterfaceMetricSample
                        {
                            DeviceIp = snapshot.DeviceIp,
                            InterfaceIndex = item.Index,
                            InterfaceName = item.Name,
                            TimestampUtc = deviceSample.TimestampUtc,
                            AdminStatus = item.AdminStatus,
                            OperStatus = item.OperStatus,
                            IncomingMbps = item.IncomingMbps,
                            OutgoingMbps = item.OutgoingMbps,
                            TotalMbps = item.TotalMbps,
                            UtilizationPercent = item.UtilizationPercent
                        }));
                    }
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                _lastSnapshotKey = key;
            }

            if (DateTimeOffset.UtcNow >= _nextRetentionUtc)
            {
                var cutoff = MetricPersistencePolicy.GetRetentionCutoff(DateTime.UtcNow, _retentionDays);
                await dbContext.InterfaceMetricSamples.Where(item => item.TimestampUtc < cutoff)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.DeviceMetricSamples.Where(item => item.TimestampUtc < cutoff)
                    .ExecuteDeleteAsync(cancellationToken);
                _nextRetentionUtc = DateTimeOffset.UtcNow.AddHours(6);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError("Ölçüm veritabanı işlemi başarısız oldu ({ExceptionType}).", exception.GetType().Name);
        }
    }
}
