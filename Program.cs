using FortiScope.Data.Entities;
using FortiScope.Services;
using FortiScope.Configuration;
using FortiScope.Data;
using FortiScope.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "fortiscope.db");

// Add services to the container.
builder.Services.AddRazorPages();
var keyDirectory = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection().SetApplicationName("FortiScope")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
builder.Services.Configure<SnmpOptions>(builder.Configuration.GetSection(SnmpOptions.SectionName));
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));
builder.Services.AddDbContextFactory<FortiScopeDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton<ISnmpMonitoringService, SnmpMonitoringService>();
builder.Services.AddHostedService<SnmpMonitoringBackgroundService>();
builder.Services.AddHostedService<MetricPersistenceBackgroundService>();
builder.Services.AddSingleton<HistoryService>();
builder.Services.AddSingleton<IEmailNotificationService, EmailNotificationService>();
builder.Services.AddHostedService<AlertNotificationBackgroundService>();

var app = builder.Build();

try
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<FortiScopeDbContext>();
    await dbContext.Database.MigrateAsync();
    if (!await dbContext.AlertSettings.AnyAsync())
    {
        dbContext.AlertSettings.Add(new AlertSettings());
        await dbContext.SaveChangesAsync();
    }
    if (!await dbContext.EmailSettings.AnyAsync())
    {
        dbContext.EmailSettings.Add(new EmailSettings());
        await dbContext.SaveChangesAsync();
    }
}
catch (Exception exception)
{
    app.Logger.LogError("SQLite migration could not be applied ({ExceptionType}). The application will continue SNMP monitoring.",
        exception.GetType().Name);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("HttpsRedirectionEnabled"))
    app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/api/devices", async (
    IDbContextFactory<FortiScopeDbContext> dbFactory,
    CancellationToken cancellationToken) =>
{
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);

    var devices = await dbContext.Devices
        .AsNoTracking()
        .OrderBy(device => device.Name)
        .ToListAsync(cancellationToken);

    return Results.Ok(devices);
});


app.MapPost("/api/devices", async (
    Device request,
    IDbContextFactory<FortiScopeDbContext> dbFactory,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Device name is required." });

    if (string.IsNullOrWhiteSpace(request.IpAddress))
        return Results.BadRequest(new { error = "IP address is required." });

    if (!System.Net.IPAddress.TryParse(request.IpAddress, out _))
        return Results.BadRequest(new { error = "Invalid IP address." });

    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);

    var exists = await dbContext.Devices
        .AnyAsync(device => device.IpAddress == request.IpAddress, cancellationToken);

    if (exists)
        return Results.Conflict(new { error = "A device with this IP address is already registered." });

    var device = new Device
    {
        Name = request.Name.Trim(),
        IpAddress = request.IpAddress.Trim(),
        SnmpVersion = string.IsNullOrWhiteSpace(request.SnmpVersion) ? "v3" : request.SnmpVersion.Trim(),
        SnmpUsername = request.SnmpUsername?.Trim(),
        AuthProtocol = request.AuthProtocol?.Trim(),
        PrivacyProtocol = request.PrivacyProtocol?.Trim(),
        Enabled = request.Enabled,
        CreatedAtUtc = DateTime.UtcNow
    };

    dbContext.Devices.Add(device);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/devices/{device.Id}", device);
});

app.MapGet("/api/devices/{id:int}", async (int id,
    IDbContextFactory<FortiScopeDbContext> dbFactory, CancellationToken cancellationToken) =>
{
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var device = await dbContext.Devices.AsNoTracking()
        .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    return device is null ? Results.NotFound(new { error = "Device not found." }) : Results.Ok(device);
});

app.MapPut("/api/devices/{id:int}", async (int id, DeviceRequest request,
    IDbContextFactory<FortiScopeDbContext> dbFactory, ISnmpMonitoringService monitoringService,
    CancellationToken cancellationToken) =>
{
    var validationError = DeviceValidator.Validate(request);
    if (validationError is not null) return Results.BadRequest(new { error = validationError });

    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var device = await dbContext.Devices.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (device is null) return Results.NotFound(new { error = "Device not found." });

    var ipAddress = request.IpAddress!.Trim();
    if (await dbContext.Devices.AsNoTracking().AnyAsync(item => item.Id != id && item.IpAddress == ipAddress,
        cancellationToken))
        return Results.Conflict(new { error = "Another device with this IP address is already registered." });

    device.Name = request.Name!.Trim();
    device.IpAddress = ipAddress;
    device.SnmpVersion = "v3";
    device.SnmpUsername = request.SnmpUsername!.Trim();
    device.AuthProtocol = "SHA1";
    device.PrivacyProtocol = "AES128";
    device.Enabled = request.Enabled;

    if (!device.Enabled)
    {
        var activeStates = await dbContext.AlertStates.Where(item => item.DeviceId == id && item.IsActive)
            .ToListAsync(cancellationToken);
        foreach (var state in activeStates)
        {
            state.IsActive = false;
            state.Severity = "normal";
        }
    }
    await dbContext.SaveChangesAsync(cancellationToken);
    monitoringService.RemoveDevice(id);
    return Results.Ok(device);
});

app.MapDelete("/api/devices/{id:int}", async (int id,
    IDbContextFactory<FortiScopeDbContext> dbFactory, ISnmpMonitoringService monitoringService,
    CancellationToken cancellationToken) =>
{
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var device = await dbContext.Devices.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (device is null) return Results.NotFound(new { error = "Device not found." });

    await dbContext.AlertStates.Where(item => item.DeviceId == id).ExecuteDeleteAsync(cancellationToken);
    dbContext.Devices.Remove(device);
    await dbContext.SaveChangesAsync(cancellationToken);
    monitoringService.RemoveDevice(id);
    return Results.Ok(new { message = "Device deleted successfully.", id });
});

app.MapPost("/api/devices/{id:int}/test", async (
    int id,
    IDbContextFactory<FortiScopeDbContext> dbFactory,
    ISnmpMonitoringService snmpService,
    CancellationToken cancellationToken) =>
{
    await using var dbContext =
        await dbFactory.CreateDbContextAsync(cancellationToken);

    var device = await dbContext.Devices
        .AsNoTracking()
        .FirstOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);

    if (device is null)
        return Results.NotFound(
            new { error = "Device not found." });

    if (!device.Enabled)
        return Results.BadRequest(
            new { error = "Device is disabled." });

    if (!string.Equals(
            device.SnmpVersion,
            "v3",
            StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(
            new { error = "Only SNMPv3 is currently supported." });
    }

    if (string.IsNullOrWhiteSpace(device.SnmpUsername))
    {
        return Results.BadRequest(
            new { error = "SNMPv3 username is required." });
    }

    var result = await snmpService.TestConnectionAsync(
        device.IpAddress,
        device.SnmpUsername,
        cancellationToken);

    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});
app.MapGet("/api/monitoring/current", (ISnmpMonitoringService service) => Results.Ok(service.GetCurrent()));
app.MapGet("/api/settings/alerts", async (IDbContextFactory<FortiScopeDbContext> dbFactory,
    CancellationToken cancellationToken) =>
{
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var settings = await dbContext.AlertSettings.AsNoTracking().OrderBy(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken) ?? new AlertSettings();
    return Results.Ok(new AlertSettingsRequest(settings.CpuWarningPercent, settings.CpuCriticalPercent,
        settings.MemoryWarningPercent, settings.MemoryCriticalPercent,
        settings.InterfaceUtilizationWarningPercent, settings.InterfaceUtilizationCriticalPercent,
        settings.OfflineTimeoutSeconds,
        settings.Enabled));
});
app.MapPut("/api/settings/alerts", async (AlertSettingsRequest request,
    IDbContextFactory<FortiScopeDbContext> dbFactory, CancellationToken cancellationToken) =>
{
    var validationError = AlertSettingsValidator.Validate(request);
    if (validationError is not null) return Results.BadRequest(new { error = validationError });

    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var settings = await dbContext.AlertSettings.OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
    if (settings is null)
    {
        settings = new AlertSettings();
        dbContext.AlertSettings.Add(settings);
    }
    settings.CpuWarningPercent = request.CpuWarningPercent;
    settings.CpuCriticalPercent = request.CpuCriticalPercent;
    settings.MemoryWarningPercent = request.MemoryWarningPercent;
    settings.MemoryCriticalPercent = request.MemoryCriticalPercent;
    settings.InterfaceUtilizationWarningPercent = request.InterfaceUtilizationWarningPercent;
    settings.InterfaceUtilizationCriticalPercent = request.InterfaceUtilizationCriticalPercent;
    settings.OfflineTimeoutSeconds = request.OfflineTimeoutSeconds;
    settings.Enabled = request.Enabled;
    settings.UpdatedAtUtc = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(request);
});
app.MapGet("/api/settings/email", async (IDbContextFactory<FortiScopeDbContext> dbFactory,
    CancellationToken cancellationToken) =>
{
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var settings = await dbContext.EmailSettings.AsNoTracking().OrderBy(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken) ?? new EmailSettings();
    return Results.Ok(new EmailSettingsResponse(settings.Enabled, settings.SmtpHost, settings.SmtpPort,
        settings.UseSsl, settings.Username, !string.IsNullOrWhiteSpace(settings.PasswordEncrypted),
        settings.FromAddress, settings.ToAddress, settings.SendWarningAlerts, settings.SendCriticalAlerts,
        settings.SendRecoveryNotifications, settings.CooldownMinutes));
});
app.MapPut("/api/settings/email", async (EmailSettingsRequest request,
    IDbContextFactory<FortiScopeDbContext> dbFactory, IDataProtectionProvider protectionProvider,
    CancellationToken cancellationToken) =>
{
    var validationError = EmailSettingsValidator.Validate(request);
    if (validationError is not null) return Results.BadRequest(new { error = validationError });
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var settings = await dbContext.EmailSettings.OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
    if (settings is null)
    {
        settings = new EmailSettings();
        dbContext.EmailSettings.Add(settings);
    }
    settings.Enabled = request.Enabled;
    settings.SmtpHost = request.SmtpHost?.Trim();
    settings.SmtpPort = request.SmtpPort;
    settings.UseSsl = request.UseSsl;
    settings.Username = request.Username?.Trim();
    if (!string.IsNullOrEmpty(request.Password))
        settings.PasswordEncrypted = protectionProvider.CreateProtector(EmailNotificationService.PasswordPurpose)
            .Protect(request.Password);
    settings.FromAddress = request.FromAddress?.Trim();
    settings.ToAddress = request.ToAddress?.Trim();
    settings.SendWarningAlerts = request.SendWarningAlerts;
    settings.SendCriticalAlerts = request.SendCriticalAlerts;
    settings.SendRecoveryNotifications = request.SendRecoveryNotifications;
    settings.CooldownMinutes = request.CooldownMinutes;
    settings.UpdatedAtUtc = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new EmailSettingsResponse(settings.Enabled, settings.SmtpHost, settings.SmtpPort,
        settings.UseSsl, settings.Username, !string.IsNullOrWhiteSpace(settings.PasswordEncrypted),
        settings.FromAddress, settings.ToAddress, settings.SendWarningAlerts, settings.SendCriticalAlerts,
        settings.SendRecoveryNotifications, settings.CooldownMinutes));
});
app.MapPost("/api/settings/email/test", async (IEmailNotificationService emailService,
    CancellationToken cancellationToken) =>
{
    var result = await emailService.SendAsync("FortiScope Test Notification",
        "FortiScope email notifications are configured correctly.", cancellationToken);
    return result.Success ? Results.Ok(new { message = "Test email sent successfully." }) :
        Results.BadRequest(new { error = result.Message });
});
app.MapGet("/api/alerts/history", async (int? deviceId, string? severity, string? eventType,
    string? alertType, string? range, int? limit,
    IDbContextFactory<FortiScopeDbContext> dbFactory, CancellationToken cancellationToken) =>
{
    range = string.IsNullOrWhiteSpace(range) ? "24h" : range;
    if (!HistoryRangeParser.TryParse(range, out var duration))
        return Results.BadRequest(new { error = "Invalid range. Supported values: 5m, 1h, 6h, 24h, 7d, 30d." });
    if (deviceId is <= 0) return Results.BadRequest(new { error = "deviceId must be positive." });
    if (!AlertHistoryQuery.IsValidSeverity(severity))
        return Results.BadRequest(new { error = "Invalid severity. Supported values: WARNING, CRITICAL, INFO." });
    if (!AlertHistoryQuery.IsValidEventType(eventType))
        return Results.BadRequest(new { error = "Invalid eventType. Supported values: OPENED, ESCALATED, RECOVERED, REMINDER." });

    var take = Math.Clamp(limit ?? 100, 1, 500);
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var query = AlertHistoryQuery.Apply(dbContext.AlertEvents.AsNoTracking(), DateTime.UtcNow - duration,
        deviceId, severity, eventType, alertType);
    return Results.Ok(await query.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id)
        .Take(take).ToListAsync(cancellationToken));
});
app.MapGet("/api/interfaces/top", async (int? limit,
    IDbContextFactory<FortiScopeDbContext> dbFactory, ISnmpMonitoringService monitoringService,
    CancellationToken cancellationToken) =>
{
    var take = limit ?? 5;
    if (take is < 1 or > 20) return Results.BadRequest(new { error = "limit must be between 1 and 20." });
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var devices = await dbContext.Devices.AsNoTracking().Where(item => item.Enabled)
        .ToListAsync(cancellationToken);
    var settings = await dbContext.AlertSettings.AsNoTracking().OrderBy(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken) ?? new AlertSettings();
    var snapshots = monitoringService.GetAllCurrent();
    var now = DateTime.UtcNow;
    var result = devices.SelectMany(device =>
    {
        if (!snapshots.TryGetValue(device.Id, out var snapshot)) return [];
        var online = snapshot.Connected && (!snapshot.LastUpdated.HasValue ||
            now - snapshot.LastUpdated.Value.UtcDateTime <= TimeSpan.FromSeconds(settings.OfflineTimeoutSeconds));
        if (!online) return [];
        return snapshot.Interfaces
            .Where(item => InterfaceTrafficAlertPolicy.IsEligible(item, device.Enabled, online, true))
            .Select(item => new TopInterfaceResponse(device.Id, device.Name, device.IpAddress,
                item.Index, item.Name, item.IncomingMbps, item.OutgoingMbps, item.TotalMbps,
                item.UtilizationPercent!.Value, InterfaceTrafficAlertPolicy.GetSeverity(item.UtilizationPercent.Value,
                    settings.InterfaceUtilizationWarningPercent, settings.InterfaceUtilizationCriticalPercent).ToUpperInvariant()));
    }).OrderByDescending(item => item.UtilizationPercent).Take(take);
    return Results.Ok(result);
});
app.MapGet("/api/devices/{id:int}/monitoring/current", async (int id,
    IDbContextFactory<FortiScopeDbContext> dbFactory, ISnmpMonitoringService service,
    CancellationToken cancellationToken) =>
{
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    if (!await dbContext.Devices.AsNoTracking().AnyAsync(device => device.Id == id, cancellationToken))
        return Results.NotFound(new { error = "Device not found." });

    var snapshot = service.GetCurrent(id);
    return snapshot is null
        ? Results.Json(new { error = "Monitoring snapshot is not available yet." }, statusCode: 503)
        : Results.Ok(snapshot);
});
app.MapGet("/api/devices/monitoring/current", async (
    IDbContextFactory<FortiScopeDbContext> dbFactory, ISnmpMonitoringService service,
    CancellationToken cancellationToken) =>
{
    await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
    var devices = await dbContext.Devices.AsNoTracking().OrderBy(device => device.Name).ToListAsync(cancellationToken);
    var snapshots = service.GetAllCurrent();
    return Results.Ok(devices.Select(device =>
    {
        MonitoringSnapshot? snapshot = null;
        if (device.Enabled) snapshots.TryGetValue(device.Id, out snapshot);
        return new
        {
            device.Id,
            device.Name,
            device.IpAddress,
            Connected = snapshot?.Connected ?? false,
            CpuUsage = snapshot?.CpuUsage,
            MemoryUsage = snapshot?.MemoryUsage,
            SessionCount = snapshot?.SessionCount,
            ErrorMessage = snapshot is not null
                ? snapshot.ErrorMessage
                : device.Enabled ? "Waiting for first poll." : "Device is disabled."
        };
    }));
});
app.MapGet("/api/history/system", async (string? range, int? deviceId, string? deviceIp,
    HistoryService service, CancellationToken cancellationToken) =>
{
    if (!HistoryRangeParser.TryParse(range, out var duration))
        return Results.BadRequest(new { error = "Invalid range. Supported values: 5m, 1h, 6h, 24h, 7d, 30d." });
    return Results.Ok(await service.GetSystemHistoryAsync(duration, deviceId, deviceIp, cancellationToken));
});
app.MapGet("/api/history/interfaces/{interfaceIndex:int}", async (int interfaceIndex, string? range,
    int? deviceId, string? deviceIp,
    HistoryService service, CancellationToken cancellationToken) =>
{
    if (interfaceIndex < 1) return Results.BadRequest(new { error = "Interface index must be positive." });
    if (!HistoryRangeParser.TryParse(range, out var duration))
        return Results.BadRequest(new { error = "Invalid range. Supported values: 5m, 1h, 6h, 24h, 7d, 30d." });
    return Results.Ok(await service.GetInterfaceHistoryAsync(interfaceIndex, duration, deviceId, deviceIp,
        cancellationToken));
});

app.Run();
