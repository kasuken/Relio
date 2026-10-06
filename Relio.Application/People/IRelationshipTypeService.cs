using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// Use cases for the current user's <see cref="RelationshipType"/> list. Scoped to the signed-in
/// user like every owned-data service (see the "User-scoped data pattern" section of AGENTS.md).
/// Issue #22 only reads the list; managing it (rename, add, remove) extends this interface in #25.
/// </summary>
public interface IRelationshipTypeService
{
    /// <summary>
    /// Lists the current user's relationship types, ordered by <see cref="RelationshipType.SortOrder"/>
    /// and then by name. The returned entities are untracked snapshots.
    /// </summary>
    Task<IReadOnlyList<RelationshipType>> ListAsync(CancellationToken cancellationToken = default);
}
