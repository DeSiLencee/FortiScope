using FortiScope.Data;
using FortiScope.Data.Entities;
using FortiScope.Models;
using Microsoft.EntityFrameworkCore;

namespace FortiScope.Services;

public sealed class AlertNotificationBackgroundService(
    ISnmpMonitoringService monitoringService,
    IEmailNotificationService emailService,
    IDbContextFactory<FortiScopeDbContext> dbContextFactory,
    ILogger<AlertNotificationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(EvaluationInterval);
        do { await EvaluateSafelyAsync(stoppingToken); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EvaluateSafelyAsync(CancellationToken cancellationToken)
    {
        try { await EvaluateAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError("Email alert evaluation failed ({ExceptionType}).", exception.GetType().Name);
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var alertSettings = await dbContext.AlertSettings.AsNoTracking().OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken) ?? new AlertSettings();
        var emailSettings = await dbContext.EmailSettings.AsNoTracking().OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken) ?? new EmailSettings();
        var devices = await dbContext.Devices.AsNoTracking().Where(item => item.Enabled)
            .ToListAsync(cancellationToken);
        var snapshots = monitoringService.GetAllCurrent();
        var now = DateTime.UtcNow;

        foreach (var device in devices)
        {
            snapshots.TryGetValue(device.Id, out var snapshot);
            if (snapshot is null) continue;
            var online = snapshot.Connected && (!snapshot.LastUpdated.HasValue ||
                now - snapshot.LastUpdated.Value.UtcDateTime <= TimeSpan.FromSeconds(alertSettings.OfflineTimeoutSeconds));

            var observations = new List<AlertObservation>
            {
                MetricObservation("CPU", snapshot?.CpuUsage, alertSettings.CpuWarningPercent,
                    alertSettings.CpuCriticalPercent, alertSettings.Enabled && online, !alertSettings.Enabled),
                MetricObservation("MEMORY", snapshot?.MemoryUsage, alertSettings.MemoryWarningPercent,
                    alertSettings.MemoryCriticalPercent, alertSettings.Enabled && online, !alertSettings.Enabled),
                new AlertObservation("DEVICE", online ? "normal" : "critical", online ? "DEVICE_ONLINE" : "DEVICE_OFFLINE",
                    null, null, online ? "Device Online" : "Device Offline")
            };
            observations.AddRange(snapshot!.Interfaces
                .Where(item => InterfaceTrafficAlertPolicy.IsEligible(item, device.Enabled, online, alertSettings.Enabled))
                .Select(item => InterfaceObservation(item, alertSettings.InterfaceUtilizationWarningPercent,
                    alertSettings.InterfaceUtilizationCriticalPercent)));

            foreach (var observation in observations)
                await ProcessObservationAsync(dbContext, emailSettings, device, observation, now, cancellationToken);
        }
    }

    private async Task ProcessObservationAsync(FortiScopeDbContext dbContext, EmailSettings emailSettings,
        Device device, AlertObservation observation, DateTime now, CancellationToken cancellationToken)
    {
        var state = await dbContext.AlertStates.FirstOrDefaultAsync(item =>
            item.DeviceId == device.Id && item.StateKey == observation.StateKey, cancellationToken);
        if (state is null && observation.Severity == "normal") return;

        var decision = AlertNotificationPolicy.Evaluate(state?.IsActive == true, state?.Severity ?? "normal",
            state?.LastNotificationUtc, observation.Severity, now, emailSettings.CooldownMinutes);
        state ??= new AlertState
        {
            DeviceId = device.Id,
            StateKey = observation.StateKey,
            AlertType = observation.AlertType,
            Severity = observation.Severity,
            FirstTriggeredUtc = now,
            LastTriggeredUtc = now
        };
        if (state.Id == 0) dbContext.AlertStates.Add(state);

        if (observation.Severity == "normal") state.IsActive = false;
        else
        {
            if (!state.IsActive) state.FirstTriggeredUtc = now;
            state.IsActive = true;
            state.LastTriggeredUtc = now;
        }
        state.AlertType = observation.AlertType;
        state.Severity = observation.Severity;
        state.LastValue = observation.Value;
        state.InterfaceIndex = observation.InterfaceIndex;
        state.InterfaceName = observation.InterfaceName;

        var alertEvent = observation.SuppressNotification ? null : AlertEventFactory.Create(device,
            observation.StateKey, decision, observation.Severity, observation.Value, observation.Threshold, now,
            observation.InterfaceIndex, observation.InterfaceName);
        if (alertEvent is not null && decision != NotificationDecision.Reminder)
        {
            dbContext.AlertEvents.Add(alertEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var shouldNotify = !observation.SuppressNotification && AlertNotificationPolicy.ShouldNotify(decision, observation.Severity,
            emailSettings.Enabled, emailSettings.SendWarningAlerts, emailSettings.SendCriticalAlerts,
            emailSettings.SendRecoveryNotifications);
        if (shouldNotify)
        {
            if (decision == NotificationDecision.Reminder && alertEvent is not null)
            {
                dbContext.AlertEvents.Add(alertEvent);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            var recovered = decision == NotificationDecision.Recovered;
            var subject = recovered
                ? $"[FortiScope] RECOVERED - {device.Name} {observation.RecoveryLabel}"
                : $"[FortiScope] {observation.Severity.ToUpperInvariant()} - {device.Name} {observation.Message}";
            var body = BuildBody(device, observation, now, recovered);
            var result = await emailService.SendAsync(subject, body, cancellationToken);
            if (result.Success)
            {
                state.LastNotificationUtc = now;
                logger.LogInformation("Email notification sent: device={DeviceName} alert={AlertType}",
                    device.Name, observation.AlertType);
            }
            else
                logger.LogWarning("Email notification failed: device={DeviceName} alert={AlertType}",
                    device.Name, observation.AlertType);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AlertObservation MetricObservation(string key, double? value, int warning, int critical,
        bool enabled, bool suppressNotification)
    {
        var severity = !enabled || !value.HasValue ? "normal" : value >= critical ? "critical" :
            value >= warning ? "warning" : "normal";
        var type = $"{key}_{severity.ToUpperInvariant()}";
        var label = key == "CPU" ? "CPU" : "Memory";
        return new(key, severity, type, value, severity == "critical" ? critical : warning,
            severity == "normal" ? $"{label} Normal" : $"{label} {value:0.#}%", $"{label} Normal",
            suppressNotification);
    }

    private static AlertObservation InterfaceObservation(NetworkInterfaceSnapshot item, int warning, int critical)
    {
        var value = item.UtilizationPercent!.Value;
        var severity = InterfaceTrafficAlertPolicy.GetSeverity(value, warning, critical);
        return new($"INTERFACE_TRAFFIC:{item.Index}", severity, "INTERFACE_TRAFFIC", value,
            severity == "critical" ? critical : warning,
            severity == "normal" ? $"{item.Name} Traffic Normal" : $"{item.Name} Traffic {value:0.#}%",
            $"{item.Name} Traffic Normal", false, item.Index, item.Name, item.IncomingMbps, item.OutgoingMbps);
    }

    private static string BuildBody(Device device, AlertObservation observation, DateTime now, bool recovered) =>
        $"Device: {device.Name}\nIP: {device.IpAddress}\n" +
        (observation.InterfaceName is null ? string.Empty :
            $"Interface: {observation.InterfaceName}\nIncoming: {observation.IncomingMbps:0.###} Mbps\nOutgoing: {observation.OutgoingMbps:0.###} Mbps\n") +
        (recovered
            ? $"Alert: {observation.RecoveryLabel}\nRecovered At: {now:O}\nCurrent Value: {FormatValue(observation.Value)}"
            : $"Severity: {observation.Severity}\nAlert: {observation.Message}\nCurrent Value: {FormatValue(observation.Value)}\nThreshold: {FormatValue(observation.Threshold)}\nDetected At: {now:O}");

    private static string FormatValue(double? value) => value.HasValue ? $"{value:0.#}%" : "N/A";

    private sealed record AlertObservation(string StateKey, string Severity, string AlertType,
        double? Value, double? Threshold, string Message, string RecoveryLabel = "Online",
        bool SuppressNotification = false, int? InterfaceIndex = null, string? InterfaceName = null,
        double? IncomingMbps = null, double? OutgoingMbps = null);
}
