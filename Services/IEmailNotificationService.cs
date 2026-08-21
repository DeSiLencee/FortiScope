namespace FortiScope.Services;

public sealed record EmailSendResult(bool Success, string Message);

public interface IEmailNotificationService
{
    Task<EmailSendResult> SendAsync(string subject, string body, CancellationToken cancellationToken = default);
}
