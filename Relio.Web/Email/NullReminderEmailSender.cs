using Microsoft.Extensions.Logging;
using Relio.Application.Reminders;

namespace Relio.Web.Email;

/// <summary>
/// The <see cref="IReminderEmailSender"/> used when <c>Email:Provider</c> is
/// <see cref="EmailOptions.NoneProvider"/> (Issue #40).
/// Sends nothing and logs at warning level, so the absence of a configured provider is visible in logs.
/// </summary>
public sealed class NullReminderEmailSender(ILogger<NullReminderEmailSender> logger) : IReminderEmailSender
{
    /// <inheritdoc />
    public Task SendImmediateReminderAsync(
        string toEmail,
        string personName,
        string reminderTitle,
        DateOnly dueDate,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default) =>
        LogSkippedAsync("immediate reminder");

    /// <inheritdoc />
    public Task SendDailyDigestAsync(
        string toEmail,
        IReadOnlyList<DigestReminderItem> items,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default) =>
        LogSkippedAsync("daily digest reminder");

    private Task LogSkippedAsync(string kind)
    {
        logger.LogWarning(
            "Not sending {EmailKind}: Email:Provider is '{Provider}'. Set Email:Provider=Smtp (and Email:Smtp:*) to enable reminder emails.",
            kind,
            EmailOptions.NoneProvider);
        return Task.CompletedTask;
    }
}
