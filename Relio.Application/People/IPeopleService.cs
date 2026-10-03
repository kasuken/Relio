using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// Use cases for the current user's <see cref="Person"/> records. Every method is scoped to the
/// signed-in user (<c>ICurrentUser</c>); there is no way to read or mutate another user's people
/// through this interface. See the "User-scoped data pattern" section of AGENTS.md for the
/// reusable pattern this interface follows, and the implementation notes in
/// <c>Relio.Data.People.PeopleService</c> for how ownership is enforced.
/// </summary>
public interface IPeopleService
{
    /// <summary>
    /// Returns the person with <paramref name="personId"/>, or <see langword="null"/> when it
    /// does not exist or does not belong to the current user. The two cases are deliberately
    /// indistinguishable to the caller.
    /// </summary>
    Task<Person?> GetAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current user's people, ordered by name. Archived people are excluded unless
    /// <paramref name="includeArchived"/> is <see langword="true"/>.
    /// </summary>
    Task<IReadOnlyList<Person>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new person owned by the current user. Throws
    /// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/> if
    /// <paramref name="request"/> references a tag id that does not belong to the current user.
    /// </summary>
    Task<Person> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the person with <paramref name="personId"/>. Returns <see langword="false"/> when
    /// it does not exist or does not belong to the current user (indistinguishable to the
    /// caller); returns <see langword="true"/> on success. Throws
    /// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/> if
    /// <paramref name="request"/> references a tag id that does not belong to the current user.
    /// </summary>
    Task<bool> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives the person with <paramref name="personId"/>. Returns <see langword="false"/> when
    /// it does not exist or does not belong to the current user; <see langword="true"/> on
    /// success. Archiving an already-archived person is a no-op that still returns
    /// <see langword="true"/>.
    /// </summary>
    Task<bool> ArchiveAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a previously archived person. Returns <see langword="false"/> when it does not
    /// exist or does not belong to the current user; <see langword="true"/> on success.
    /// </summary>
    Task<bool> RestoreAsync(Guid personId, CancellationToken cancellationToken = default);
}
