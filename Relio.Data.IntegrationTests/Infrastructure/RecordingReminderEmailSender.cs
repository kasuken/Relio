using Relio.Application.Reminders;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// A thread-safe test delivery sink: records every reminder email handed to it so a test can assert
/// exactly what each recipient received, and can fail chosen sends.
/// </summary>
internal sealed class RecordingReminderEmailSender : IReminderEmailSender
{
    private readonly object _gate = new();
    private readonly List<(string Email, string PersonName, string Title, DateOnly DueDate, bool Digest)> _deliveries = [];
    private readonly List<(string Email, int Items)> _digests = [];
    private int _callCount;

    /// <summary>The exception to throw for a send (recipient, 1-based call number), or null to succeed.</summary>
    public Func<string, int, Exception?>? FailWhen { get; set; }

    /// <summary>Every send attempt, successful or not.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>The (title, due date) pairs delivered to <paramref name="email"/>, immediate and digest items alike.</summary>
    public List<(string Title, DateOnly DueDate)> DeliveriesFor(string email)
    {
        lock (_gate)
        {
            return _deliveries
                .Where(d => d.Email == email)
                .Select(d => (d.Title, d.DueDate))
                .ToList();
        }
    }

    /// <summary>How many digest emails went to <paramref name="email"/>.</summary>
    public int DigestCountFor(string email)
    {
        lock (_gate)
        {
            return _digests.Count(d => d.Email == email);
        }
    }

    /// <inheritdoc />
    public Task SendImmediateReminderAsync(
        string toEmail,
        string personName,
        string reminderTitle,
        DateOnly dueDate,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        BeginSend(toEmail);
        lock (_gate)
        {
            _deliveries.Add((toEmail, personName, reminderTitle, dueDate, false));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendDailyDigestAsync(
        string toEmail,
        IReadOnlyList<DigestReminderItem> items,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        BeginSend(toEmail);
        lock (_gate)
        {
            _digests.Add((toEmail, items.Count));
            foreach (var item in items)
            {
                _deliveries.Add((toEmail, item.PersonName, item.Title, item.DueDate, true));
            }
        }

        return Task.CompletedTask;
    }

    private void BeginSend(string toEmail)
    {
        var call = Interlocked.Increment(ref _callCount);
        if (FailWhen?.Invoke(toEmail, call) is { } failure)
        {
            throw failure;
        }
    }
}
