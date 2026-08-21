using FortiScope.Data.Entities;

namespace FortiScope.Services;

public static class AlertHistoryQuery
{
    public static IQueryable<AlertEvent> Apply(IQueryable<AlertEvent> query, DateTime cutoffUtc,
        int? deviceId, string? severity, string? eventType, string? alertType)
    {
        query = query.Where(item => item.OccurredAtUtc >= cutoffUtc);
        if (deviceId.HasValue) query = query.Where(item => item.DeviceId == deviceId.Value);
        if (!string.IsNullOrWhiteSpace(severity))
        {
            var normalized = severity.Trim().ToUpperInvariant();
            query = query.Where(item => item.Severity == normalized);
        }
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var normalized = eventType.Trim().ToUpperInvariant();
            query = query.Where(item => item.EventType == normalized);
        }
        if (!string.IsNullOrWhiteSpace(alertType))
        {
            var normalized = alertType.Trim().ToUpperInvariant();
            query = query.Where(item => item.AlertType == normalized);
        }
        return query;
    }

    public static bool IsValidSeverity(string? value) => IsValid(value, "WARNING", "CRITICAL", "INFO");
    public static bool IsValidEventType(string? value) => IsValid(value, "OPENED", "ESCALATED", "RECOVERED", "REMINDER");

    private static bool IsValid(string? value, params string[] allowed) =>
        string.IsNullOrWhiteSpace(value) || allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}
