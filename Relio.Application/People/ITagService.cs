using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// Use cases for the current user's <see cref="Tag"/> list. Scoped to the signed-in user like every
/// owned-data service (see the "User-scoped data pattern" section of AGENTS.md). Tags are also
/// created as a side effect of saving a person (<see cref="UpdatePersonRequest.NewTagNames"/>,
/// issue #24); this service reads the list for the tag picker and manages it in settings (issue #25).
/// </summary>
public interface ITagService
{
    /// <summary>
    /// Lists the current user's tags, ordered by name. The returned entities are untracked
    /// snapshots (the people they are attached to are not loaded).
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current user's tags with how many people have each (archived people included),
    /// ordered by name.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<IReadOnlyList<TagUsage>> ListWithUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a tag. The name is trimmed and its whitespace collapsed.
    /// </summary>
    /// <returns>The created tag (an untracked snapshot).</returns>
    /// <exception cref="LabelValidationException">
    /// The name is blank, too long, or the user already has a tag with that name (compared
    /// case-insensitively). Thrown before anything is saved.
    /// </exception>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<Tag> CreateAsync(string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a tag. People refer to it by id, so it changes for everyone who has it. Changing
    /// only the casing of its own name is allowed.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there is no such tag for the current user (missing and
    /// belonging to someone else are the same answer).
    /// </returns>
    /// <exception cref="LabelValidationException">
    /// The name is blank, too long, or another of the user's tags already has it.
    /// </exception>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<bool> RenameAsync(Guid tagId, string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a tag and takes it off every person who has it. The people themselves are not
    /// changed.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there is no such tag for the current user (missing and
    /// belonging to someone else are the same answer).
    /// </returns>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<bool> DeleteAsync(Guid tagId, CancellationToken cancellationToken = default);
}
