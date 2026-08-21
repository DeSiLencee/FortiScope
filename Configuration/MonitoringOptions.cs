namespace FortiScope.Configuration;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";
    public int PersistenceIntervalSeconds { get; init; } = 10;
    public int RetentionDays { get; init; } = 30;
    public int AlertEventRetentionDays { get; init; } = 90;
}
