namespace Relio.Application.People;

/// <summary>
/// Input for <c>IPeopleService.CreateAsync</c>. Only <see cref="FirstName"/> is required; text is
/// trimmed and blank optional text is stored as nothing. See <see cref="PersonProfileRules"/> for
/// what is rejected.
/// </summary>
public sealed record CreatePersonRequest : IPersonProfileInput
{
    /// <summary>The person's first name. Required.</summary>
    public required string FirstName { get; init; }

    /// <summary>The person's last name. Optional.</summary>
    public string? LastName { get; init; }

    /// <summary>What the user calls the person, if it is not their first name. Optional.</summary>
    public string? Nickname { get; init; }

    /// <summary>
    /// The id of one of the current user's relationship types, or null for none. An id that is not
    /// the current user's makes the service throw
    /// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/>.
    /// </summary>
    public Guid? RelationshipTypeId { get; init; }

    /// <summary>The day of the birthday, 1 to 31. Needs <see cref="BirthdayMonth"/>.</summary>
    public int? BirthdayDay { get; init; }

    /// <summary>The month of the birthday, 1 to 12. Needs <see cref="BirthdayDay"/>.</summary>
    public int? BirthdayMonth { get; init; }

    /// <summary>The year of birth, or null when unknown. Only with a day and a month, and not in the future.</summary>
    public int? BirthdayYear { get; init; }

    /// <summary>How the user met the person. Optional.</summary>
    public string? HowWeMet { get; init; }

    /// <summary>Anything else that helps the user remember the person. Optional.</summary>
    public string? Details { get; init; }

    /// <summary>
    /// Ids of existing tags to attach. Every id must belong to the current user; otherwise the
    /// service throws <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/>.
    /// </summary>
    public IReadOnlyCollection<Guid>? TagIds { get; init; }

    /// <summary>
    /// Names of tags to attach. A name that matches one of the user's tags (ignoring case) attaches
    /// that tag; any other is created for the user in the same save. Blank names are ignored.
    /// </summary>
    public IReadOnlyCollection<string>? NewTagNames { get; init; }

    /// <summary>
    /// The person's contact methods, in order. A new person has none yet, so every
    /// <see cref="ContactMethodInput.Id"/> must be <see langword="null"/>: one that is not makes
    /// the service throw <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/>.
    /// </summary>
    public IReadOnlyList<ContactMethodInput>? ContactMethods { get; init; }

    /// <summary>Optional stay-in-touch cadence in days (issue #41).</summary>
    public int? StayInTouchCadenceDays { get; init; }

    /// <summary>Whether birthday reminders are disabled specifically for this person (issue #38).</summary>
    public bool BirthdayReminderDisabled { get; init; }

    /// <summary>Per-person lead time override for birthday reminders in days (issue #38).</summary>
    public int? BirthdayReminderLeadDays { get; init; }
}
