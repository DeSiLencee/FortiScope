using FortiScope.Configuration;
using FortiScope.Data;
using FortiScope.Data.Entities;
using FortiScope.Models;
using FortiScope.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortiScope.Tests;

public sealed class DeviceManagementTests
{
    [Fact]
    public async Task GetDeviceById_ReturnsExistingDevice()
    {
        await using var db = CreateContext();
        var device = AddDevice(db, "HQ", "192.168.1.1");
        await db.SaveChangesAsync();
        Assert.Equal("HQ", (await db.Devices.AsNoTracking().SingleAsync(item => item.Id == device.Id)).Name);
    }

    [Fact]
    public async Task UpdateValidDevice_PersistsChanges()
    {
        await using var db = CreateContext();
        var device = AddDevice(db, "HQ", "192.168.1.1");
        await db.SaveChangesAsync();
        var request = ValidRequest() with { Name = "HQ-NEW", IpAddress = "192.168.1.2" };
        Assert.Null(DeviceValidator.Validate(request));
        device.Name = request.Name!; device.IpAddress = request.IpAddress!;
        await db.SaveChangesAsync();
        Assert.Equal("HQ-NEW", (await db.Devices.SingleAsync()).Name);
    }

    [Fact]
    public async Task UpdateDuplicateIp_IsDetectedForAnotherDevice()
    {
        await using var db = CreateContext();
        var first = AddDevice(db, "HQ", "192.168.1.1");
        AddDevice(db, "Branch", "192.168.1.2");
        await db.SaveChangesAsync();
        Assert.True(await db.Devices.AnyAsync(item => item.Id != first.Id && item.IpAddress == "192.168.1.2"));
    }

    [Fact]
    public void UpdateInvalidIp_IsRejected() =>
        Assert.NotNull(DeviceValidator.Validate(ValidRequest() with { IpAddress = "not-an-ip" }));

    [Fact]
    public async Task Device_CanBeDisabledAndReEnabled()
    {
        await using var db = CreateContext();
        var device = AddDevice(db, "HQ", "192.168.1.1");
        await db.SaveChangesAsync();
        device.Enabled = false; await db.SaveChangesAsync();
        Assert.False((await db.Devices.SingleAsync()).Enabled);
        device.Enabled = true; await db.SaveChangesAsync();
        Assert.True((await db.Devices.SingleAsync()).Enabled);
    }

    [Fact]
    public async Task DeleteExistingDevice_RetainsAlertHistory()
    {
        await using var db = CreateContext();
        var device = AddDevice(db, "HQ", "192.168.1.1");
        await db.SaveChangesAsync();
        db.AlertEvents.Add(new AlertEvent { DeviceId = device.Id, DeviceName = device.Name,
            DeviceIp = device.IpAddress, AlertType = "CPU_HIGH", Severity = "CRITICAL",
            EventType = "OPENED", Message = "Test", OccurredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.Devices.Remove(device); await db.SaveChangesAsync();
        Assert.Empty(db.Devices);
        Assert.Single(db.AlertEvents);
    }

    [Fact]
    public async Task DeleteNonexistentDevice_CanBeIdentifiedForNotFound()
    {
        await using var db = CreateContext();
        Assert.Null(await db.Devices.FindAsync(999));
    }

    [Fact]
    public async Task DisabledDevice_IsRemovedFromPollingSnapshots()
    {
        var service = new SnmpMonitoringService(Options.Create(new SnmpOptions()),
            NullLogger<SnmpMonitoringService>.Instance);
        await service.PollAsync(7, "192.168.1.1", "", "HQ");
        Assert.NotNull(service.GetCurrent(7));
        service.SetActiveDevices(new HashSet<int>());
        Assert.Null(service.GetCurrent(7));
    }

    private static FortiScopeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FortiScopeDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        var db = new FortiScopeDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static Device AddDevice(FortiScopeDbContext db, string name, string ip)
    {
        var device = new Device { Name = name, IpAddress = ip, SnmpUsername = "fortiscope",
            AuthProtocol = "SHA1", PrivacyProtocol = "AES128" };
        db.Devices.Add(device);
        return device;
    }

    private static DeviceRequest ValidRequest() =>
        new("HQ", "192.168.1.1", "v3", "fortiscope", "SHA1", "AES128", true);
}
