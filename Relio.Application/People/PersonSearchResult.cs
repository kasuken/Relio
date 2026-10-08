namespace Relio.Application.People;

/// <summary>
/// A brief match returned by <see cref="IPeopleService.SearchAsync"/> for instant name lookups
/// in the global search bar or quick search inputs.
/// </summary>
/// <param name="Id">The person's identifier.</param>
/// <param name="FirstName">The first name.</param>
/// <param name="LastName">The last name, if any.</param>
/// <param name="RelationshipTypeName">The name of their relationship type, or <see langword="null"/> when none is chosen.</param>
/// <param name="IsArchived">Whether the person is archived.</param>
public sealed record PersonSearchResult(
    Guid Id,
    string FirstName,
    string? LastName,
    string? RelationshipTypeName,
    bool IsArchived)
{
    /// <summary>The person's full display name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(LastName)
        ? FirstName
        : $"{FirstName} {LastName}".Trim();
}
