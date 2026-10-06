using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// One row of the people list: only what a row shows. Deliberately not a <see cref="Person"/> -
/// the list never needs their how-we-met text, details or birthday, and not loading them keeps
/// private text off a screen that does not show it (data minimisation). No nickname either.
/// </summary>
/// <param name="Id">The person's id, for the link to their profile.</param>
/// <param name="FirstName">The person's first name.</param>
/// <param name="LastName">The person's last name, if they have one.</param>
/// <param name="RelationshipTypeName">The name of their relationship type, or <see langword="null"/> when none is chosen.</param>
/// <param name="LastContactedOn">The calendar date of their most recent interaction, or <see langword="null"/> when there has never been one.</param>
/// <param name="IsArchived">Whether the person is archived. Only ever <see langword="true"/> when the list was asked to include archived people.</param>
/// <param name="CreatedAtUtc">When the profile was created (UTC); what <see cref="PeopleSort.RecentlyAdded"/> orders by.</param>
public sealed record PersonListItem(
    Guid Id,
    string FirstName,
    string? LastName,
    string? RelationshipTypeName,
    DateOnly? LastContactedOn,
    bool IsArchived,
    DateTime CreatedAtUtc)
{
    /// <summary>The name as the list shows it: first and last name, or just the first name.</summary>
    public string DisplayName => Person.FormatDisplayName(FirstName, LastName);
}
