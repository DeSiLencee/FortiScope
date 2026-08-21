using FortiScope.Models;

namespace FortiScope.Services;

public interface ISnmpMonitoringService
{
    MonitoringSnapshot GetCurrent();

    MonitoringSnapshot? GetCurrent(int deviceId);

    IReadOnlyDictionary<int, MonitoringSnapshot> GetAllCurrent();

    void SetActiveDevices(IReadOnlySet<int> deviceIds);

    void RemoveDevice(int deviceId);

    Task PollAsync(CancellationToken cancellationToken = default);

    Task PollAsync(int deviceId, string host, string username, string deviceName,
        CancellationToken cancellationToken = default);

    Task<SnmpConnectionTestResult> TestConnectionAsync(
        string host,
        string username,
        CancellationToken cancellationToken = default);
}
