namespace Relio.Domain;

/// <summary>
/// A person the current user has a relationship with: the profile the rest of Relio hangs off.
/// Built on the user-scoped data pattern (issue #10) and fleshed out by issue #22 with a nickname,
/// a relationship type, a birthday (the year is optional), how the user met them and free-form
/// details, and by issue #24 with contact methods and tags. Later issues add interactions, notes
/// and reminders.
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
    /// The calendar date, in the owner's time zone, of the most recent interaction with this person,
    /// or <see langword="null"/> when there has never been one. A calendar date, never a UTC instant:
    /// it is not converted to or from UTC (see "Dates and time zones" in AGENTS.md).
    /// </summary>
    /// <remarks>
    /// Read-only for everything in the People feature: <c>CreatePersonRequest</c> and
    /// <c>UpdatePersonRequest</c> never carry it, so editing a profile cannot change it. Issue #34
    /// (log an interaction) is the one feature that maintains it, from the interactions it records.
    /// Until then it is only ever set by seed data.
    /// </remarks>
    public DateOnly? LastContactedOn { get; set; }

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

    /// <summary>
    /// How to reach this person (issue #24), ordered by <see cref="ContactMethod.SortOrder"/>. Each
    /// has the same owner as the person; they are deleted with it.
    /// </summary>
    public ICollection<ContactMethod> ContactMethods { get; set; } = new List<ContactMethod>();

    /// <summary>
    /// Reminders scheduled for this person (epic #36). Deleted with the person.
    /// </summary>
    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();

    /// <summary>
    /// Difficult moments recorded for this person (epic #42). Deleted with the person.
    /// </summary>
    public ICollection<DifficultMoment> DifficultMoments { get; set; } = new List<DifficultMoment>();

    /// <summary>
    /// Optional stay-in-touch cadence in days (issue #41), e.g. 30 days. When null, no cadence is enforced.
    /// </summary>
    public int? StayInTouchCadenceDays { get; set; }

    /// <summary>
    /// Whether birthday reminders are disabled specifically for this person (issue #38).
    /// </summary>
    public bool BirthdayReminderDisabled { get; set; }

    /// <summary>
    /// Per-person lead time override for birthday reminders in days (issue #38).
    /// When null, the user's global lead time is used.
    /// </summary>
    public int? BirthdayReminderLeadDays { get; set; }

    /// <summary>A display-friendly name composed from <see cref="FirstName"/> and <see cref="LastName"/>.</summary>
    public string DisplayName => FormatDisplayName(FirstName, LastName);

    /// <summary>
    /// Composes a display name from a first and an optional last name. The one place the rule
    /// lives, shared with list projections that carry the names but not a whole <see cref="Person"/>.
    /// </summary>
    public static string FormatDisplayName(string firstName, string? lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName : $"{firstName} {lastName}";

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
