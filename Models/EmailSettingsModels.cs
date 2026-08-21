namespace FortiScope.Models;

public sealed record EmailSettingsRequest(bool Enabled, string? SmtpHost, int SmtpPort, bool UseSsl,
    string? Username, string? Password, string? FromAddress, string? ToAddress,
    bool SendWarningAlerts, bool SendCriticalAlerts, bool SendRecoveryNotifications, int CooldownMinutes);

public sealed record EmailSettingsResponse(bool Enabled, string? SmtpHost, int SmtpPort, bool UseSsl,
    string? Username, bool HasPassword, string? FromAddress, string? ToAddress,
    bool SendWarningAlerts, bool SendCriticalAlerts, bool SendRecoveryNotifications, int CooldownMinutes);
