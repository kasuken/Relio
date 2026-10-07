using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// One of the current user's people that might be the person being added or renamed. It carries the
/// user's own data and is only ever shown back to them; never log it.
/// </summary>
/// <param name="Id">The person's id (link to <c>/people/{id}</c>).</param>
/// <param name="FirstName">The first name as stored.</param>
/// <param name="LastName">The last name as stored, or <see langword="null"/>.</param>
/// <param name="IsArchived">Whether the person is archived. Archived people are included: an archived duplicate is still a duplicate.</param>
/// <param name="Reasons">Why this person matched, in <see cref="PossibleDuplicateReason"/> order. Never empty.</param>
public sealed record PossibleDuplicate(
    Guid Id,
    string FirstName,
    string? LastName,
    bool IsArchived,
    IReadOnlyList<PossibleDuplicateReason> Reasons)
{
    /// <summary>The name to show, built the same way as <see cref="Person.DisplayName"/>.</summary>
    public string DisplayName => Person.FormatDisplayName(FirstName, LastName);
}
