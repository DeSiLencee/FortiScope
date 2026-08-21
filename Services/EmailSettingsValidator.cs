using System.Net.Mail;
using FortiScope.Models;

namespace FortiScope.Services;

public static class EmailSettingsValidator
{
    public static string? Validate(EmailSettingsRequest request)
    {
        if (request.SmtpPort is < 1 or > 65535) return "SMTP port must be between 1 and 65535.";
        if (request.CooldownMinutes is < 1 or > 1440) return "Cooldown must be between 1 and 1440 minutes.";
        if (!request.Enabled) return null;
        if (string.IsNullOrWhiteSpace(request.SmtpHost)) return "SMTP host is required when email notifications are enabled.";
        if (!IsValidAddress(request.FromAddress)) return "A valid From address is required.";
        if (!IsValidAddress(request.ToAddress)) return "A valid To address is required.";
        return null;
    }

    private static bool IsValidAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return new MailAddress(value).Address == value.Trim(); }
        catch (FormatException) { return false; }
    }
}
