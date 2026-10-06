using Relio.Application.Paging;

namespace Relio.Application.People;

/// <summary>
/// A page of the people list together with how many people the current user has in total, split
/// into active and archived. The totals describe the whole account, not the page and not the
/// filter, so a screen can tell "you have nobody" from "everyone is archived" without asking again.
/// </summary>
/// <param name="People">The requested page.</param>
/// <param name="ActiveCount">How many of the current user's people are not archived.</param>
/// <param name="ArchivedCount">How many of the current user's people are archived.</param>
public sealed record PeopleListResult(PagedResult<PersonListItem> People, int ActiveCount, int ArchivedCount)
{
    /// <summary>Whether the current user has any people at all, archived or not.</summary>
    public bool HasAnyone => ActiveCount + ArchivedCount > 0;
}
