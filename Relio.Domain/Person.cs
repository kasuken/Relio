namespace Relio.Domain;

/// <summary>
/// A person the current user has a relationship with: the profile the rest of Relio hangs off.
/// Built on the user-scoped data pattern (issue #10) and fleshed out by issue #22 with a nickname,
/// a relationship type, a birthday (the year is optional), how the user met them and free-form
/// details. Later issues in epic #21 add contact methods, interactions, notes and reminders.
/// </summary>
public sealed class Person : OwnedEntity
{
    /// <summary>The longest <see cref="FirstName"/> Relio stores, in characters.</summary>
    public const int FirstNameMaxLength = 100;

    /// <summary>The longest <see cref="LastName"/> Relio stores, in characters.</summary>
    public const int LastNameMaxLength = 100;

    /// <summary>The longest <see cref="Nickname"/> Relio stores, in characters.</summary>
    public const int NicknameMaxLength = 100;

    /// <summary>The longest <see cref="HowWeMet"/> Relio stores, in characters.</summary>
    public const int HowWeMetMaxLength = 1000;

    /// <summary>The longest <see cref="Details"/> Relio stores, in characters.</summary>
    public const int DetailsMaxLength = 4000;

    /// <summary>The person's first name. Required - the only thing a profile needs.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The person's last name. Optional - not everyone is tracked with a surname.</summary>
    public string? LastName { get; set; }

    /// <summary>What the user calls them, when it is not their first name. Optional.</summary>
    public string? Nickname { get; set; }

    /// <summary>
    /// The id of the user's <see cref="RelationshipType"/> for this person, or <see langword="null"/>
    /// when none is chosen. Always one of the same owner's types - enforced by
    /// <c>Relio.Data.People.PeopleService</c>, not by the database.
    /// </summary>
    public Guid? RelationshipTypeId { get; set; }

    /// <summary>The relationship type, when loaded. Never set by callers; assign <see cref="RelationshipTypeId"/>.</summary>
    public RelationshipType? RelationshipType { get; set; }

    /// <summary>The year of birth, or <see langword="null"/> when unknown. Only meaningful with <see cref="BirthdayMonth"/> and <see cref="BirthdayDay"/>.</summary>
    public int? BirthdayYear { get; set; }

    /// <summary>The month of the birthday (1 to 12), or <see langword="null"/> when no birthday is recorded.</summary>
    public int? BirthdayMonth { get; set; }

    /// <summary>The day of the birthday (1 to 31), or <see langword="null"/> when no birthday is recorded.</summary>
    public int? BirthdayDay { get; set; }

    /// <summary>A place, a moment, who introduced you: how the user met this person. Optional.</summary>
    public string? HowWeMet { get; set; }

    /// <summary>Anything else that helps the user remember them. Optional, may span several lines.</summary>
    public string? Details { get; set; }

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

    /// <summary>
    /// The birthday, built from the three stored columns, or <see langword="null"/> when none is
    /// recorded. Calendar data only: no time zone and never converted to or from UTC.
    /// </summary>
    /// <remarks>
    /// Computed and ignored by EF Core (like <see cref="DisplayName"/>), so never use it inside a
    /// LINQ query that runs against the database - filter on <see cref="BirthdayMonth"/> and
    /// <see cref="BirthdayDay"/> instead. Fully qualified because the property and the type share a name.
    /// </remarks>
    public Domain.Birthday? Birthday =>
        BirthdayMonth is int month && BirthdayDay is int day && Domain.Birthday.TryCreate(month, day, BirthdayYear, out var birthday)
            ? birthday
            : null;
}
