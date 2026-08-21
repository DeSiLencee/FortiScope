using FortiScope.Data.Entities;
using FortiScope.Models;
using FortiScope.Services;
using Xunit;

namespace FortiScope.Tests;

public sealed class EmailNotificationTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Defaults_AreSafeAndExpected()
    {
        var settings = new EmailSettings();
        Assert.False(settings.Enabled);
        Assert.Equal(587, settings.SmtpPort);
        Assert.True(settings.UseSsl);
        Assert.False(settings.SendWarningAlerts);
        Assert.True(settings.SendCriticalAlerts);
        Assert.True(settings.SendRecoveryNotifications);
        Assert.Equal(15, settings.CooldownMinutes);
    }

    [Fact]
    public void EnabledSettings_RequireValidSmtpAndAddresses()
    {
        Assert.Null(EmailSettingsValidator.Validate(ValidRequest()));
        Assert.NotNull(EmailSettingsValidator.Validate(ValidRequest() with { SmtpHost = "" }));
        Assert.NotNull(EmailSettingsValidator.Validate(ValidRequest() with { FromAddress = "invalid" }));
        Assert.NotNull(EmailSettingsValidator.Validate(ValidRequest() with { ToAddress = "invalid" }));
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(65536, 15)]
    [InlineData(587, 0)]
    [InlineData(587, 1441)]
    public void PortAndCooldown_OutOfRange_AreRejected(int port, int cooldown) =>
        Assert.NotNull(EmailSettingsValidator.Validate(ValidRequest() with
        {
            SmtpPort = port,
            CooldownMinutes = cooldown
        }));

    [Fact]
    public void ActiveAlertInsideCooldown_DoesNotSendDuplicate() =>
        Assert.Equal(NotificationDecision.None,
            AlertNotificationPolicy.Evaluate(true, "critical", Now.AddMinutes(-2), "critical", Now, 15));

    [Fact]
    public void ActiveAlertAfterCooldown_ProducesReminder() =>
        Assert.Equal(NotificationDecision.Reminder,
            AlertNotificationPolicy.Evaluate(true, "critical", Now.AddMinutes(-16), "critical", Now, 15));

    [Fact]
    public void WarningToCritical_ProducesEscalationInsideCooldown() =>
        Assert.Equal(NotificationDecision.Escalated,
            AlertNotificationPolicy.Evaluate(true, "warning", Now.AddMinutes(-1), "critical", Now, 15));

    [Fact]
    public void CriticalToNormal_ProducesRecovery() =>
        Assert.Equal(NotificationDecision.Recovered,
            AlertNotificationPolicy.Evaluate(true, "critical", Now.AddMinutes(-1), "normal", Now, 15));

    [Fact]
    public void EmailDisabled_SuppressesEveryNotification()
    {
        Assert.False(AlertNotificationPolicy.ShouldNotify(NotificationDecision.Opened, "critical",
            false, true, true, true));
        Assert.False(AlertNotificationPolicy.ShouldNotify(NotificationDecision.Recovered, "normal",
            false, true, true, true));
    }

    private static EmailSettingsRequest ValidRequest() => new(true, "smtp.example.com", 587, true,
        "alerts@example.com", "secret", "alerts@example.com", "admin@example.com",
        false, true, true, 15);
}
