namespace FortiScope.Data.Entities;

public sealed class InterfaceMetricSample
{
    public long Id { get; set; }
    public required string DeviceIp { get; set; }
    public int InterfaceIndex { get; set; }
    public required string InterfaceName { get; set; }
    public DateTime TimestampUtc { get; set; }
    public int? AdminStatus { get; set; }
    public int? OperStatus { get; set; }
    public double IncomingMbps { get; set; }
    public double OutgoingMbps { get; set; }
    public double TotalMbps { get; set; }
    public double? UtilizationPercent { get; set; }
}
