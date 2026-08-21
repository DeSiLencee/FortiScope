using System.Net;
using System.Net.Mail;
using FortiScope.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FortiScope.Services;

public sealed class EmailNotificationService(
    IDbContextFactory<FortiScopeDbContext> dbContextFactory,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<EmailNotificationService> logger) : IEmailNotificationService
{
    public const string PasswordPurpose = "FortiScope.EmailSettings.Password.v1";

    public async Task<EmailSendResult> SendAsync(string subject, string body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var settings = await dbContext.EmailSettings.AsNoTracking().OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (settings is null || !settings.Enabled) return new(false, "Email notifications are disabled.");
            if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.FromAddress) ||
                string.IsNullOrWhiteSpace(settings.ToAddress)) return new(false, "Email settings are incomplete.");

            using var message = new MailMessage(settings.FromAddress, settings.ToAddress, subject, body);
            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                var password = string.IsNullOrWhiteSpace(settings.PasswordEncrypted) ? string.Empty :
                    dataProtectionProvider.CreateProtector(PasswordPurpose).Unprotect(settings.PasswordEncrypted);
                client.Credentials = new NetworkCredential(settings.Username, password);
            }
            await client.SendMailAsync(message, cancellationToken);
            return new(true, "Email sent successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Email delivery failed ({ExceptionType}).", exception.GetType().Name);
            return new(false, "Email could not be sent. Check the SMTP settings and network connection.");
        }
    }
}
