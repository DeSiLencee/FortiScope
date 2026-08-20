using FortiScope.Models;

namespace FortiScope.Services;

public interface ISnmpMonitoringService
{
    MonitoringSnapshot GetCurrent();
    Task PollAsync(CancellationToken cancellationToken = default);
}
