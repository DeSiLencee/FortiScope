using FortiScope.Models;
using FortiScope.Services;
using Xunit;

namespace FortiScope.Tests;

public sealed class PersistencePolicyTests
{
    [Theory]
    [InlineData("5m", 5)]
    [InlineData("1h", 60)]
    [InlineData("6h", 360)]
    [InlineData("24h", 1440)]
    [InlineData("7d", 10080)]
    [InlineData("30d", 43200)]
    public void HistoryRangeParser_ParsesSupportedRanges(string value, int expectedMinutes)
    {
        Assert.True(HistoryRangeParser.TryParse(value, out var range));
        Assert.Equal(expectedMinutes, range.TotalMinutes);
    }

    [Fact]
    public void HistoryRangeParser_RejectsUnsupportedRange()
    {
        Assert.False(HistoryRangeParser.TryParse("2h", out _));
    }

    [Fact]
    public void OfflineSample_NullsMetricValues()
    {
        var snapshot = CreateSnapshot(connected: false);
        var sample = MetricPersistencePolicy.CreateDeviceSample(snapshot, DateTimeOffset.UtcNow);

        Assert.False(sample.Connected);
        Assert.Null(sample.CpuUsage);
        Assert.Null(sample.MemoryUsage);
        Assert.Null(sample.SessionCount);
    }

    [Fact]
    public void SameSnapshot_ProducesSameDeduplicationKey()
    {
        var snapshot = CreateSnapshot(connected: true);

        var key = MetricPersistencePolicy.GetSnapshotKey(snapshot);
        Assert.False(MetricPersistencePolicy.ShouldPersist(key, key));
    }

    [Fact]
    public void RetentionCutoff_SubtractsConfiguredDays()
    {
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now.AddDays(-30), MetricPersistencePolicy.GetRetentionCutoff(now, 30));
    }

    private static MonitoringSnapshot CreateSnapshot(bool connected) => new(
        "FortiGate", "192.168.64.2", connected, 25, 40, 1000, [],
        new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero), connected ? null : "No connection");
}
