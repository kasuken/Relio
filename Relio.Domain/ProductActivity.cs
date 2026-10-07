namespace Relio.Domain;

/// <summary>
/// Minimal owner-attributable metadata used only to calculate aggregate product metrics (issue
/// #62). It is pseudonymous, not anonymous: it is linked to its owner and is deleted after a
/// fixed 90-day lifetime.
/// </summary>
/// <remarks>
/// This entity deliberately contains no account identity, relationship content, request, device,
/// session, URL, or event-history data. Its retention expiry is set from the cohort start and
/// never slides when activity is recorded.
/// </remarks>
public sealed class ProductActivity : OwnedEntity
{
    /// <summary>The UTC calendar date on which this user's first observed activity was recorded.</summary>
    public DateOnly CohortStartedOnUtc { get; set; }

    /// <summary>The most recent UTC calendar date on which activity was observed.</summary>
    public DateOnly LastActiveOnUtc { get; set; }

    /// <summary>
    /// Whether activity was observed from cohort age day 30 through day 59, inclusive.
    /// </summary>
    public bool ReturnedInDays30To59 { get; set; }

    /// <summary>
    /// The fixed UTC instant at which this contribution expires: midnight UTC at the start of
    /// cohort age day 90.
    /// </summary>
    public DateTime RetentionExpiresAtUtc { get; set; }
}
