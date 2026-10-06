namespace Relio.Application.People;

/// <summary>
/// Input for <c>IPeopleService.UpdateAsync</c>. It replaces the person's profile fields entirely -
/// every field is written, so a field left out clears it - and the supplied <see cref="TagIds"/>
/// replace the person's current tag set entirely. See <see cref="PersonProfileRules"/> for what is
/// rejected.
/// </summary>
public sealed record UpdatePersonRequest : IPersonProfileInput
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
    /// Ids of existing tags that should replace the person's current tags. Every id must belong to
    /// the current user; otherwise the service throws
    /// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/>.
    /// </summary>
    public IReadOnlyCollection<Guid>? TagIds { get; init; }
}
