namespace FortiScope.Data.Entities;

public sealed class AlertEvent
{
    public long Id { get; set; }
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceIp { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double? MetricValue { get; set; }
    public double? ThresholdValue { get; set; }
    public int? InterfaceIndex { get; set; }
    public string? InterfaceName { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
