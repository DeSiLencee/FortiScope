using FortiScope.Models;
using FortiScope.Services;
using Xunit;

namespace FortiScope.Tests;

public sealed class InterfaceTrafficAlertTests
{
    private static readonly DateTime Now = new(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UtilizationBelowWarning_IsNormal() =>
        Assert.Equal("normal", InterfaceTrafficAlertPolicy.GetSeverity(69, 70, 90));

    [Fact]
    public void WarningThresholdCrossed_OpensWarning() =>
        Assert.Equal(NotificationDecision.Opened,
            AlertNotificationPolicy.Evaluate(false, "normal", null,
                InterfaceTrafficAlertPolicy.GetSeverity(75, 70, 90), Now, 15));

    [Fact]
    public void WarningContinues_HasNoDuplicateInsideCooldown() =>
        Assert.Equal(NotificationDecision.None,
            AlertNotificationPolicy.Evaluate(true, "warning", Now.AddMinutes(-1), "warning", Now, 15));

    [Fact]
    public void WarningToCritical_Escalates() =>
        Assert.Equal(NotificationDecision.Escalated,
            AlertNotificationPolicy.Evaluate(true, "warning", Now.AddMinutes(-1), "critical", Now, 15));

    [Fact]
    public void CriticalContinues_HasNoDuplicateInsideCooldown() =>
        Assert.Equal(NotificationDecision.None,
            AlertNotificationPolicy.Evaluate(true, "critical", Now.AddMinutes(-1), "critical", Now, 15));

    [Theory]
    [InlineData("critical")]
    [InlineData("warning")]
    public void ActiveToNormal_Recovers(string previousSeverity) =>
        Assert.Equal(NotificationDecision.Recovered,
            AlertNotificationPolicy.Evaluate(true, previousSeverity, Now.AddMinutes(-1), "normal", Now, 15));

    [Fact]
    public void SeparateInterfaces_UseSeparateStateKeys()
    {
        Assert.NotEqual(StateKey(1), StateKey(2));
    }

    [Fact]
    public void SameInterfaceIndexOnSeparateDevices_UsesSeparateIdentity()
    {
        Assert.NotEqual((1, StateKey(1)), (2, StateKey(1)));
    }

    [Theory]
    [InlineData("Sanal", 1, false, 80d, 1000L, true, true, true)]
    [InlineData("Fiziksel", 2, false, 80d, 1000L, true, true, true)]
    [InlineData("Fiziksel", 1, false, 80d, 1000L, false, true, true)]
    [InlineData("Fiziksel", 1, false, 80d, 1000L, true, false, true)]
    [InlineData("Fiziksel", 1, false, 80d, 1000L, true, true, false)]
    [InlineData("Fiziksel", 1, true, 80d, 1000L, true, true, true)]
    [InlineData("Fiziksel", 1, false, null, 1000L, true, true, true)]
    [InlineData("Fiziksel", 1, false, 80d, null, true, true, true)]
    public void IneligibleInterfaces_AreIgnored(string type, int operStatus, bool measuring,
        double? utilization, long? speed, bool deviceEnabled, bool online, bool alertsEnabled)
    {
        Assert.False(InterfaceTrafficAlertPolicy.IsEligible(Interface(type, operStatus, measuring,
            utilization, speed), deviceEnabled, online, alertsEnabled));
    }

    [Fact]
    public void FullyMeasuredPhysicalUpInterface_IsEligible() =>
        Assert.True(InterfaceTrafficAlertPolicy.IsEligible(Interface("Fiziksel", 1, false, 75, 1000),
            true, true, true));

    private static string StateKey(int index) => $"INTERFACE_TRAFFIC:{index}";

    private static NetworkInterfaceSnapshot Interface(string type, int operStatus, bool measuring,
        double? utilization, long? speed) => new(1, "port1", type, null, 1, operStatus,
        operStatus == 1 ? "Aktif" : "Kapalı", speed, 10, 5, 15, utilization, measuring, null);
}
