namespace FortiScope.Services;

public sealed class SnmpMonitoringBackgroundService(
    ISnmpMonitoringService monitoringService,
    ILogger<SnmpMonitoringBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FortiGate SNMP izleme servisi başlatıldı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await monitoringService.PollAsync(stoppingToken);

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
