using Relio.Application.Reminders;

namespace Relio.Data.Tests.Reminders;

/// <summary>
/// In-memory fake implementation of <see cref="IReminderEmailSender"/> for unit tests.
/// </summary>
public sealed class FakeReminderEmailSender : IReminderEmailSender
{
    public List<ImmediateEmailRecord> ImmediateEmails { get; } = [];
    public List<DailyDigestRecord> DigestEmails { get; } = [];

    public Task SendImmediateReminderAsync(
        string toEmail,
        string personName,
        string reminderTitle,
        DateOnly dueDate,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        ImmediateEmails.Add(new ImmediateEmailRecord(toEmail, personName, reminderTitle, dueDate, unsubscribeToken));
        return Task.CompletedTask;
    }

    public Task SendDailyDigestAsync(
        string toEmail,
        IReadOnlyList<DigestReminderItem> items,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        DigestEmails.Add(new DailyDigestRecord(toEmail, items, unsubscribeToken));
        return Task.CompletedTask;
    }

    public sealed record ImmediateEmailRecord(
        string ToEmail,
        string PersonName,
        string ReminderTitle,
        DateOnly DueDate,
        string? UnsubscribeToken);

    public sealed record DailyDigestRecord(
        string ToEmail,
        IReadOnlyList<DigestReminderItem> Items,
        string? UnsubscribeToken);
}
