using FortiScope.Data.Entities;
using FortiScope.Models;

namespace FortiScope.Services;

public static class MetricPersistencePolicy
{
    public static bool ShouldPersist(string snapshotKey, string? lastSnapshotKey) =>
        !string.Equals(snapshotKey, lastSnapshotKey, StringComparison.Ordinal);

    public static string GetSnapshotKey(MonitoringSnapshot snapshot) => snapshot.Connected
        ? $"online:{snapshot.DeviceIp}:{snapshot.LastUpdated?.UtcTicks}"
        : $"offline:{snapshot.DeviceIp}:{snapshot.ErrorMessage}";

    public static DateTime GetRetentionCutoff(DateTime utcNow, int retentionDays) =>
        utcNow.AddDays(-Math.Max(1, retentionDays));

    public static DeviceMetricSample CreateDeviceSample(MonitoringSnapshot snapshot, DateTimeOffset persistedAtUtc)
    {
        var timestamp = snapshot.Connected && snapshot.LastUpdated.HasValue
            ? snapshot.LastUpdated.Value.UtcDateTime
            : persistedAtUtc.UtcDateTime;

        return new DeviceMetricSample
        {
            DeviceIp = snapshot.DeviceIp,
            DeviceName = snapshot.DeviceName,
            TimestampUtc = timestamp,
            Connected = snapshot.Connected,
            CpuUsage = snapshot.Connected ? snapshot.CpuUsage : null,
            MemoryUsage = snapshot.Connected ? snapshot.MemoryUsage : null,
            SessionCount = snapshot.Connected ? snapshot.SessionCount : null,
            ErrorMessage = snapshot.ErrorMessage
        };
    }
}
