namespace FortiScope.Data.Entities;

public sealed class AlertSettings
{
    public int Id { get; set; }
    public int CpuWarningPercent { get; set; } = 70;
    public int CpuCriticalPercent { get; set; } = 85;
    public int MemoryWarningPercent { get; set; } = 75;
    public int MemoryCriticalPercent { get; set; } = 90;
    public int InterfaceUtilizationWarningPercent { get; set; } = 70;
    public int InterfaceUtilizationCriticalPercent { get; set; } = 90;
    public int OfflineTimeoutSeconds { get; set; } = 30;
    public bool Enabled { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
