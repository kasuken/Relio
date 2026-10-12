using Relio.Application.Reminders;

namespace Relio.Data.Tests.Reminders;

/// <summary>
/// In-memory fake implementation of <see cref="IReminderEmailSender"/> for unit tests.
/// </summary>
public sealed class FakeReminderEmailSender : IReminderEmailSender
{
    private int _callCount;

    public List<ImmediateEmailRecord> ImmediateEmails { get; } = [];
    public List<DailyDigestRecord> DigestEmails { get; } = [];

    /// <summary>Every send attempt so far, successful or not.</summary>
    public int CallCount => _callCount;

    /// <summary>
    /// Returns the exception to throw for a send (recipient, 1-based call number), or null to let it
    /// succeed. Evaluated before anything is recorded, so a failed send leaves no record.
    /// </summary>
    public Func<string, int, Exception?>? FailWhen { get; set; }

    /// <summary>Runs after a send was recorded, with its 1-based call number (to cancel a token, say).</summary>
    public Action<int>? OnSend { get; set; }

    /// <summary>Every text value of every recorded email, for privacy assertions.</summary>
    public IEnumerable<string> AllRecordedText =>
        ImmediateEmails
            .SelectMany(e => new[] { e.ToEmail, e.PersonName, e.ReminderTitle, e.DueDate.ToString("O"), e.UnsubscribeToken ?? string.Empty })
            .Concat(DigestEmails.SelectMany(d => d.Items
                .SelectMany(i => new[] { i.PersonName, i.Title, i.DueDate.ToString("O") })
                .Append(d.ToEmail)
                .Append(d.UnsubscribeToken ?? string.Empty)));

    public Task SendImmediateReminderAsync(
        string toEmail,
        string personName,
        string reminderTitle,
        DateOnly dueDate,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        var call = BeginSend(toEmail);
        ImmediateEmails.Add(new ImmediateEmailRecord(toEmail, personName, reminderTitle, dueDate, unsubscribeToken));
        OnSend?.Invoke(call);
        return Task.CompletedTask;
    }

    public Task SendDailyDigestAsync(
        string toEmail,
        IReadOnlyList<DigestReminderItem> items,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        var call = BeginSend(toEmail);
        DigestEmails.Add(new DailyDigestRecord(toEmail, items, unsubscribeToken));
        OnSend?.Invoke(call);
        return Task.CompletedTask;
    }

    private int BeginSend(string toEmail)
    {
        var call = Interlocked.Increment(ref _callCount);
        if (FailWhen?.Invoke(toEmail, call) is { } failure)
        {
            throw failure;
        }

        return call;
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
