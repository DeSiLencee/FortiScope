using FortiScope.Data.Entities;

namespace FortiScope.Services;

public static class AlertEventFactory
{
    public static AlertEvent? Create(Device device, string stateKey, NotificationDecision decision,
        string severity, double? metricValue, double? thresholdValue, DateTime occurredAtUtc,
        int? interfaceIndex = null, string? interfaceName = null)
    {
        if (decision == NotificationDecision.None) return null;

        var eventType = decision.ToString().ToUpperInvariant();
        var eventSeverity = decision == NotificationDecision.Recovered ? "INFO" : severity.ToUpperInvariant();
        var alertType = stateKey switch
        {
            "CPU" => "CPU_HIGH",
            "MEMORY" => "MEMORY_HIGH",
            "DEVICE" => "DEVICE_OFFLINE",
            _ when stateKey.StartsWith("INTERFACE_TRAFFIC:", StringComparison.Ordinal) => "INTERFACE_TRAFFIC",
            _ => stateKey.ToUpperInvariant()
        };

        return new AlertEvent
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            DeviceIp = device.IpAddress,
            AlertType = alertType,
            Severity = eventSeverity,
            EventType = eventType,
            Message = BuildMessage(stateKey, decision, metricValue, thresholdValue, severity, interfaceName),
            MetricValue = metricValue,
            ThresholdValue = thresholdValue,
            InterfaceIndex = interfaceIndex,
            InterfaceName = interfaceName,
            OccurredAtUtc = occurredAtUtc
        };
    }

    private static string BuildMessage(string stateKey, NotificationDecision decision, double? value,
        double? threshold, string severity, string? interfaceName)
    {
        if (stateKey == "DEVICE")
            return decision == NotificationDecision.Recovered
                ? "Device became reachable again."
                : "Device became unreachable.";

        if (stateKey.StartsWith("INTERFACE_TRAFFIC:", StringComparison.Ordinal))
        {
            var interfaceLabel = string.IsNullOrWhiteSpace(interfaceName) ? "Interface" : interfaceName;
            if (decision == NotificationDecision.Recovered)
                return $"{interfaceLabel} utilization returned to normal at {Format(value)}%.";
            if (decision == NotificationDecision.Escalated)
                return $"{interfaceLabel} utilization escalated to CRITICAL at {Format(value)}%, above threshold {Format(threshold)}%.";
            if (decision == NotificationDecision.Reminder)
                return $"{interfaceLabel} utilization remains at {Format(value)}%, above threshold {Format(threshold)}%.";
            return $"{interfaceLabel} utilization reached {Format(value)}%, above {severity.ToLowerInvariant()} threshold {Format(threshold)}%.";
        }

        var label = stateKey == "CPU" ? "CPU usage" : "Memory usage";
        if (decision == NotificationDecision.Recovered)
            return $"{label} returned to normal at {Format(value)}%.";
        if (decision == NotificationDecision.Escalated)
            return $"{label} escalated to {severity.ToUpperInvariant()} at {Format(value)}%, above threshold {Format(threshold)}%.";
        if (decision == NotificationDecision.Reminder)
            return $"{label} remains at {Format(value)}%, above threshold {Format(threshold)}%.";
        return $"{label} reached {Format(value)}%, above {severity.ToLowerInvariant()} threshold {Format(threshold)}%.";
    }

    private static string Format(double? value) => value?.ToString("0.#") ?? "unknown";
}
