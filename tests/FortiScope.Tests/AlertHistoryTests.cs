using FortiScope.Data.Entities;
using FortiScope.Services;
using Xunit;

namespace FortiScope.Tests;

public sealed class AlertHistoryTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Device Device = new() { Id = 1, Name = "FortiGate-HQ", IpAddress = "192.168.1.1" };

    [Fact]
    public void NormalToWarning_CreatesOneOpenedEvent()
    {
        var decision = AlertNotificationPolicy.Evaluate(false, "normal", null, "warning", Now, 15);
        var alertEvent = AlertEventFactory.Create(Device, "CPU", decision, "warning", 80, 70, Now);

        Assert.NotNull(alertEvent);
        Assert.Equal("OPENED", alertEvent.EventType);
        Assert.Equal("CPU_HIGH", alertEvent.AlertType);
        Assert.Equal("WARNING", alertEvent.Severity);
    }

    [Fact]
    public void WarningContinuesInsideCooldown_CreatesNoDuplicateEvent()
    {
        var decision = AlertNotificationPolicy.Evaluate(true, "warning", Now.AddMinutes(-1), "warning", Now, 15);
        Assert.Null(AlertEventFactory.Create(Device, "CPU", decision, "warning", 81, 70, Now));
    }

    [Fact]
    public void WarningToCritical_CreatesEscalatedEvent()
    {
        var decision = AlertNotificationPolicy.Evaluate(true, "warning", Now.AddMinutes(-1), "critical", Now, 15);
        var alertEvent = AlertEventFactory.Create(Device, "MEMORY", decision, "critical", 94, 90, Now);
        Assert.Equal("ESCALATED", alertEvent?.EventType);
        Assert.Equal("CRITICAL", alertEvent?.Severity);
    }

    [Fact]
    public void CriticalContinuesInsideCooldown_CreatesNoDuplicateEvent()
    {
        var decision = AlertNotificationPolicy.Evaluate(true, "critical", Now.AddMinutes(-1), "critical", Now, 15);
        Assert.Equal(NotificationDecision.None, decision);
    }

    [Fact]
    public void CriticalToNormal_CreatesRecoveredInfoEvent()
    {
        var decision = AlertNotificationPolicy.Evaluate(true, "critical", Now.AddMinutes(-1), "normal", Now, 15);
        var alertEvent = AlertEventFactory.Create(Device, "CPU", decision, "normal", 45, 70, Now);
        Assert.Equal("RECOVERED", alertEvent?.EventType);
        Assert.Equal("INFO", alertEvent?.Severity);
    }

    [Fact]
    public void DeviceOfflineAndOnline_CreateOpenedAndRecoveryEvents()
    {
        var opened = AlertEventFactory.Create(Device, "DEVICE",
            AlertNotificationPolicy.Evaluate(false, "normal", null, "critical", Now, 15),
            "critical", null, null, Now);
        var recovered = AlertEventFactory.Create(Device, "DEVICE",
            AlertNotificationPolicy.Evaluate(true, "critical", Now, "normal", Now.AddMinutes(2), 15),
            "normal", null, null, Now.AddMinutes(2));

        Assert.Equal("DEVICE_OFFLINE", opened?.AlertType);
        Assert.Equal("OPENED", opened?.EventType);
        Assert.Equal("RECOVERED", recovered?.EventType);
    }

    [Fact]
    public void QueryFilters_ByDeviceSeverityEventTypeAndRange()
    {
        var events = new[]
        {
            Event(1, "CRITICAL", "OPENED", "CPU_HIGH", Now.AddHours(-1)),
            Event(2, "WARNING", "OPENED", "MEMORY_HIGH", Now.AddHours(-1)),
            Event(1, "INFO", "RECOVERED", "CPU_HIGH", Now.AddDays(-2))
        }.AsQueryable();

        var result = AlertHistoryQuery.Apply(events, Now.AddHours(-24), 1, "critical", "opened", "cpu_high").ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].DeviceId);
    }

    private static AlertEvent Event(int deviceId, string severity, string eventType, string alertType,
        DateTime occurredAtUtc) => new()
        { DeviceId = deviceId, Severity = severity, EventType = eventType, AlertType = alertType, OccurredAtUtc = occurredAtUtc };
}
