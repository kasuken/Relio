namespace Relio.Application.People;

/// <summary>
/// One row of the relationship types settings list: a type and how many people have it.
/// </summary>
/// <param name="Id">The type's id.</param>
/// <param name="Name">The type's display name.</param>
/// <param name="SortOrder">Where the type appears in the list the user picks from.</param>
/// <param name="PeopleCount">How many of the user's people have this type, archived people included.</param>
public sealed record RelationshipTypeUsage(Guid Id, string Name, int SortOrder, int PeopleCount);
