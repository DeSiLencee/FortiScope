namespace FortiScope.Services;

public enum NotificationDecision { None, Opened, Reminder, Escalated, Recovered }

public static class AlertNotificationPolicy
{
    public static NotificationDecision Evaluate(bool wasActive, string previousSeverity,
        DateTime? lastNotificationUtc, string currentSeverity, DateTime utcNow, int cooldownMinutes)
    {
        if (currentSeverity == "normal") return wasActive ? NotificationDecision.Recovered : NotificationDecision.None;
        if (!wasActive) return NotificationDecision.Opened;
        if (Rank(currentSeverity) > Rank(previousSeverity)) return NotificationDecision.Escalated;
        if (lastNotificationUtc is null || utcNow - lastNotificationUtc >= TimeSpan.FromMinutes(cooldownMinutes))
            return NotificationDecision.Reminder;
        return NotificationDecision.None;
    }

    public static bool ShouldNotify(NotificationDecision decision, string severity, bool emailEnabled,
        bool sendWarnings, bool sendCritical, bool sendRecovery) => decision switch
    {
        NotificationDecision.Recovered => emailEnabled && sendRecovery,
        NotificationDecision.Opened or NotificationDecision.Reminder or NotificationDecision.Escalated =>
            emailEnabled && (severity == "critical" ? sendCritical : sendWarnings),
        _ => false
    };

    private static int Rank(string severity) => severity == "critical" ? 2 : severity == "warning" ? 1 : 0;
}
