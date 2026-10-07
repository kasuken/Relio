namespace Relio.Application.People;

/// <summary>
/// What to look for when checking whether a person is already in the user's list
/// (<see cref="IPeopleService.FindPossibleDuplicatesAsync"/>). Personal data: never log it.
/// </summary>
public sealed record PossibleDuplicateQuery
{
    /// <summary>The first name being entered. Blank means only the contact methods are compared.</summary>
    public string? FirstName { get; init; }

    /// <summary>The last name being entered, or null.</summary>
    public string? LastName { get; init; }

    /// <summary>The nickname being entered, or null.</summary>
    public string? Nickname { get; init; }

    /// <summary>
    /// The contact methods being entered. Only <see cref="Relio.Domain.ContactMethodKind.Email"/> and
    /// <see cref="Relio.Domain.ContactMethodKind.Phone"/> are compared; an unusable value (an email
    /// without an <c>@</c>, a phone number with fewer than three digits) is ignored.
    /// </summary>
    public IReadOnlyList<ContactMethodInput>? ContactMethods { get; init; }

    /// <summary>
    /// A person to leave out, normally the one being edited, so nobody is reported as a duplicate of
    /// themselves. It excludes; it is not an assigned foreign id, so it is not ownership-checked:
    /// another user's id excludes nothing, because another user's people are never candidates.
    /// </summary>
    public Guid? ExcludePersonId { get; init; }

    /// <summary>Builds a query from a create or update request (names, nickname and contact methods).</summary>
    public static PossibleDuplicateQuery FromProfile(IPersonProfileInput input, Guid? excludePersonId = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new PossibleDuplicateQuery
        {
            FirstName = input.FirstName,
            LastName = input.LastName,
            Nickname = input.Nickname,
            ContactMethods = input.ContactMethods,
            ExcludePersonId = excludePersonId,
        };
    }
}
