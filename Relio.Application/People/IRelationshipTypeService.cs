using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// Use cases for the current user's <see cref="RelationshipType"/> list: reading it for the person
/// form (issue #22) and managing it in settings (issue #25). Scoped to the signed-in user like
/// every owned-data service (see the "User-scoped data pattern" section of AGENTS.md). Nothing here
/// ever creates the default types: they are seeded when the account is created, so a default the
/// user removed stays removed.
/// </summary>
public interface IRelationshipTypeService
{
    /// <summary>
    /// Lists the current user's relationship types, ordered by <see cref="RelationshipType.SortOrder"/>
    /// and then by name. The returned entities are untracked snapshots.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<IReadOnlyList<RelationshipType>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current user's relationship types with how many people have each (archived people
    /// included), in the same order as <see cref="ListAsync"/>.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<IReadOnlyList<RelationshipTypeUsage>> ListWithUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a relationship type, last in the list. The name is trimmed and its whitespace collapsed.
    /// </summary>
    /// <returns>The created type (an untracked snapshot).</returns>
    /// <exception cref="LabelValidationException">
    /// The name is blank, too long, or the user already has a type with that name (compared
    /// case-insensitively). Thrown before anything is saved.
    /// </exception>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<RelationshipType> CreateAsync(string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a relationship type. People refer to it by id, so everyone who has it shows the new
    /// name. Changing only the casing of its own name is allowed.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there is no such type for the current user (missing and
    /// belonging to someone else are the same answer).
    /// </returns>
    /// <exception cref="LabelValidationException">
    /// The name is blank, too long, or another of the user's types already has it.
    /// </exception>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<bool> RenameAsync(Guid relationshipTypeId, string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a relationship type. Every person who has it (archived people too) is moved to
    /// <paramref name="reassignToId"/>, or left without a type when that is <see langword="null"/>;
    /// the moves and the removal are one save. Nothing recreates the type afterwards.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there is no such type for the current user (missing and
    /// belonging to someone else are the same answer).
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="reassignToId"/> is the type being removed.</exception>
    /// <exception cref="Relio.Application.Ownership.ForeignEntityNotOwnedException">
    /// <paramref name="reassignToId"/> is not one of the current user's types (checked only once
    /// the type being removed was found, so the two cases are never distinguishable).
    /// </exception>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<bool> DeleteAsync(Guid relationshipTypeId, Guid? reassignToId, CancellationToken cancellationToken = default);
}
