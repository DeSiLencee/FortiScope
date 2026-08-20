namespace FortiScope.Data.Entities;

public sealed class DeviceMetricSample
{
    public long Id { get; set; }
    public required string DeviceIp { get; set; }
    public required string DeviceName { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool Connected { get; set; }
    public int? CpuUsage { get; set; }
    public int? MemoryUsage { get; set; }
    public long? SessionCount { get; set; }
    public string? ErrorMessage { get; set; }
}
