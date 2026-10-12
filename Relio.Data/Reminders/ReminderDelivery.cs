namespace Relio.Data.Reminders;

/// <summary>
/// The claim ledger behind reminder and birthday emails (issue #121): one row per occurrence
/// delivered, so a retry, a restart or a competing worker can never send the same one twice.
/// </summary>
/// <remarks>
/// <para>
/// Delivery infrastructure next to <c>RelioUser</c>, like <c>UserSubscription</c>: it holds ids and
/// dates only, so it is deliberately not an <c>IOwnedEntity</c>. It is not exported, a restore never
/// creates one, and the runner purges it after a short retention. Account erasure deletes it in the
/// same save (the foreign key to the user is NO ACTION, so a late claim is rejected, not orphaned).
/// </para>
/// <para>
/// The unique index on (<see cref="UserId"/>, <see cref="Kind"/>, <see cref="SourceId"/>,
/// <see cref="OccurrenceDate"/>) is the authority: a worker claims before it sends, and a worker
/// that loses the race loses its save. <see cref="SourceId"/> is a bare id with no foreign key, so
/// the row is not a person child; deleting a person leaves its birthday row until the purge.
/// </para>
/// </remarks>
public sealed class ReminderDelivery
{
    /// <summary>The row id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The Relio user (an <c>AspNetUsers</c> id) the notification was for.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Whether the source is a reminder or a person's birthday.</summary>
    public ReminderDeliveryKind Kind { get; set; }

    /// <summary>The reminder id or the person id, depending on <see cref="Kind"/>.</summary>
    public Guid SourceId { get; set; }

    /// <summary>
    /// The occurrence delivered: a reminder's effective due date, or the observed birthday date
    /// (28 February in non-leap years for a 29 February birthday).
    /// </summary>
    public DateOnly OccurrenceDate { get; set; }

    /// <summary>The user's calendar day on which the claim was made.</summary>
    public DateOnly DeliveryDate { get; set; }

    /// <summary>Whether the send has been recorded.</summary>
    public ReminderDeliveryStatus Status { get; set; } = ReminderDeliveryStatus.Pending;

    /// <summary>When the claim was made (UTC), from the injected clock.</summary>
    public DateTime ClaimedAtUtc { get; set; }

    /// <summary>When the send was recorded (UTC), from the injected clock.</summary>
    public DateTime? SentAtUtc { get; set; }
}
