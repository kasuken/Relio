namespace Relio.Application.People;

/// <summary>
/// One row of the tags settings list: a tag and how many people have it.
/// </summary>
/// <param name="Id">The tag's id.</param>
/// <param name="Name">The tag's display name.</param>
/// <param name="PeopleCount">How many of the user's people have this tag, archived people included.</param>
public sealed record TagUsage(Guid Id, string Name, int PeopleCount);
