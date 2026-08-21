using FortiScope.Data;
using Microsoft.EntityFrameworkCore;

namespace FortiScope.Services;

public sealed class SnmpMonitoringBackgroundService(
    ISnmpMonitoringService monitoringService,
    IDbContextFactory<FortiScopeDbContext> dbContextFactory,
    ILogger<SnmpMonitoringBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FortiGate SNMP monitoring service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);
                var devices = await dbContext.Devices.AsNoTracking()
                    .Where(device => device.Enabled)
                    .OrderBy(device => device.Id)
                    .ToListAsync(stoppingToken);
                monitoringService.SetActiveDevices(devices.Select(device => device.Id).ToHashSet());

                foreach (var device in devices)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    logger.LogInformation("Polling FortiGate {DeviceName} ({Host})", device.Name, device.IpAddress);
                    await monitoringService.PollAsync(device.Id, device.IpAddress,
                        device.SnmpUsername ?? string.Empty, device.Name, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("FortiGate device list could not be read ({ExceptionType}).", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
