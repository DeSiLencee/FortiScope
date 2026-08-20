namespace FortiScope.Models;

public sealed record MonitoringSnapshot(
    string DeviceName,
    string DeviceIp,
    bool Connected,
    int? CpuUsage,
    int? MemoryUsage,
    long? SessionCount,
    IReadOnlyList<NetworkInterfaceSnapshot> Interfaces,
    DateTimeOffset? LastUpdated,
    string? ErrorMessage);
