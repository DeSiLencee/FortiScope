using FortiScope.Services;
using Xunit;

namespace FortiScope.Tests;

public sealed class InterfaceRateCalculatorTests
{
    [Fact]
    public void Calculate_UsesCounterDeltaAndElapsedTime()
    {
        var result = InterfaceRateCalculator.Calculate(
            3_000_000, 2_000_000, 1_000_000, 1_000_000, TimeSpan.FromSeconds(2), 1_000);

        Assert.Equal(8, result.IncomingMbps);
        Assert.Equal(4, result.OutgoingMbps);
        Assert.Equal(12, result.TotalMbps);
        Assert.Equal(0.8, result.UtilizationPercent);
        Assert.False(result.IsMeasuring);
    }

    [Fact]
    public void Calculate_CounterReset_DoesNotProduceNegativeTraffic()
    {
        var result = InterfaceRateCalculator.Calculate(
            500, 800, 1_000, 1_000, TimeSpan.FromSeconds(2), 100);

        Assert.Equal(0, result.TotalMbps);
        Assert.True(result.IsMeasuring);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Calculate_NonPositiveElapsedTime_ReturnsMeasuring(double seconds)
    {
        var result = InterfaceRateCalculator.Calculate(
            2_000, 2_000, 1_000, 1_000, TimeSpan.FromSeconds(seconds), 100);

        Assert.Equal(0, result.TotalMbps);
        Assert.True(result.IsMeasuring);
    }

    [Fact]
    public void Calculate_UnknownSpeed_LeavesUtilizationNull()
    {
        var result = InterfaceRateCalculator.Calculate(
            2_000_000, 2_000_000, 1_000_000, 1_000_000, TimeSpan.FromSeconds(1), null);

        Assert.Equal(16, result.TotalMbps);
        Assert.Null(result.UtilizationPercent);
        Assert.False(result.IsMeasuring);
    }

    [Fact]
    public void Calculate_LowTraffic_PreservesSubKbpsPrecision()
    {
        var result = InterfaceRateCalculator.Calculate(
            1_000_420, 1_000_000, 1_000_000, 1_000_000, TimeSpan.FromSeconds(8), 100);

        Assert.Equal(0.00042, result.IncomingMbps, 8);
        Assert.Equal(0.00042, result.TotalMbps, 8);
    }
}
