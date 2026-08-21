namespace FortiScope.Data.Entities;

public sealed class AlertState
{
    public long Id { get; set; }
    public int DeviceId { get; set; }
    public required string StateKey { get; set; }
    public required string AlertType { get; set; }
    public bool IsActive { get; set; }
    public DateTime FirstTriggeredUtc { get; set; }
    public DateTime LastTriggeredUtc { get; set; }
    public DateTime? LastNotificationUtc { get; set; }
    public double? LastValue { get; set; }
    public int? InterfaceIndex { get; set; }
    public string? InterfaceName { get; set; }
    public required string Severity { get; set; }
}
