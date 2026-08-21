using FortiScope.Data.Entities;
using FortiScope.Models;
using FortiScope.Services;
using Xunit;

namespace FortiScope.Tests;

public sealed class AlertSettingsTests
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var settings = new AlertSettings();
        Assert.Equal(70, settings.CpuWarningPercent);
        Assert.Equal(85, settings.CpuCriticalPercent);
        Assert.Equal(75, settings.MemoryWarningPercent);
        Assert.Equal(90, settings.MemoryCriticalPercent);
        Assert.Equal(70, settings.InterfaceUtilizationWarningPercent);
        Assert.Equal(90, settings.InterfaceUtilizationCriticalPercent);
        Assert.Equal(30, settings.OfflineTimeoutSeconds);
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void ValidSettings_AreAccepted() =>
        Assert.Null(AlertSettingsValidator.Validate(Request()));

    [Fact]
    public void CpuCriticalNotGreaterThanWarning_IsRejected() =>
        Assert.NotNull(AlertSettingsValidator.Validate(Request(cpuWarning: 85, cpuCritical: 85)));

    [Fact]
    public void MemoryCriticalNotGreaterThanWarning_IsRejected() =>
        Assert.NotNull(AlertSettingsValidator.Validate(Request(memoryWarning: 90, memoryCritical: 90)));

    [Fact]
    public void InterfaceCriticalNotGreaterThanWarning_IsRejected() =>
        Assert.NotNull(AlertSettingsValidator.Validate(Request(interfaceWarning: 90, interfaceCritical: 90)));

    [Fact]
    public void OfflineTimeoutBelowMinimum_IsRejected() =>
        Assert.NotNull(AlertSettingsValidator.Validate(Request(offlineTimeout: 9)));

    [Fact]
    public void OfflineTimeoutAboveMaximum_IsRejected() =>
        Assert.NotNull(AlertSettingsValidator.Validate(Request(offlineTimeout: 3601)));

    private static AlertSettingsRequest Request(int cpuWarning = 70, int cpuCritical = 85,
        int memoryWarning = 75, int memoryCritical = 90, int interfaceWarning = 70,
        int interfaceCritical = 90, int offlineTimeout = 30) =>
        new(cpuWarning, cpuCritical, memoryWarning, memoryCritical, interfaceWarning, interfaceCritical,
            offlineTimeout, true);
}
