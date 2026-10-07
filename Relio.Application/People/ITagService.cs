using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// Use cases for the current user's <see cref="Tag"/> list. Scoped to the signed-in user like every
/// owned-data service (see the "User-scoped data pattern" section of AGENTS.md). Issue #24 only
/// reads the list, for the tag picker; tags are created as a side effect of saving a person
/// (<see cref="UpdatePersonRequest.NewTagNames"/>), and issue #25 extends this interface with
/// rename and remove.
/// </summary>
public interface ITagService
{
    /// <summary>
    /// Lists the current user's tags, ordered by name. The returned entities are untracked
    /// snapshots (the people they are attached to are not loaded).
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default);
}
