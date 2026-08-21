namespace FortiScope.Data.Entities;

public sealed class EmailSettings
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? PasswordEncrypted { get; set; }
    public string? FromAddress { get; set; }
    public string? ToAddress { get; set; }
    public bool SendWarningAlerts { get; set; }
    public bool SendCriticalAlerts { get; set; } = true;
    public bool SendRecoveryNotifications { get; set; } = true;
    public int CooldownMinutes { get; set; } = 15;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
