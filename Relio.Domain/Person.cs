namespace Relio.Domain;

/// <summary>
/// A person the current user has a relationship with. This is the first product entity built on
/// the user-scoped data pattern (issue #10); epic #21 will flesh it out with more detail (photos,
/// relationship type, contact info, etc.).
/// </summary>
public sealed class Person : OwnedEntity
{
    /// <summary>The person's first name. Required.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The person's last name. Optional - not everyone is tracked with a surname.</summary>
    public string? LastName { get; set; }

    /// <summary>
    /// The person's birthday, as a calendar date with no time-of-day or time zone component.
    /// Birthdays are interpreted in the owning user's time zone (see epic #12), never converted
    /// to or from UTC.
    /// </summary>
    public DateOnly? Birthday { get; set; }

    /// <summary>
    /// Whether this person is archived. Active views exclude archived people by default.
    /// Archiving is reversible via <see cref="ArchivedAtUtc"/> and <c>IPeopleService.RestoreAsync</c>.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>UTC timestamp recorded when the person was archived; null while active.</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    /// <summary>
    /// Tags attached to this person. Every tag must belong to the same owner as the person -
    /// enforced by <c>Relio.Data.People.PeopleService</c>, not by the database.
    /// </summary>
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();

    /// <summary>A display-friendly name composed from <see cref="FirstName"/> and <see cref="LastName"/>.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}
