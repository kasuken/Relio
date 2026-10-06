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
    /// indistinguishable to the caller. The person comes back as an untracked snapshot with its
    /// tags and <see cref="Person.RelationshipType"/> loaded, so a later read always sees what is
    /// in the database now.
    /// </summary>
    Task<Person?> GetAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists <b>every</b> one of the current user's people, ordered by name, as untracked snapshots
    /// with their <see cref="Person.RelationshipType"/> loaded. Archived people are excluded unless
    /// <paramref name="includeArchived"/> is <see langword="true"/>. Unpaged and loads whole
    /// profiles, so it is for callers that need all of them (pickers, exports); the people list
    /// screen uses <see cref="ListPageAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Person>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the current user's people for the people list, in the order
    /// <see cref="PeopleListQuery.Sort"/> asks for, together with how many people the user has
    /// (<see cref="PeopleListResult.ActiveCount"/> and <see cref="PeopleListResult.ArchivedCount"/>
    /// - the user's own people only, whatever the filter). Archived people are excluded unless
    /// <see cref="PeopleListQuery.IncludeArchived"/> is set. A page below 1 or past the last page
    /// is not an error: the nearest page that exists comes back, and
    /// <see cref="Relio.Application.Paging.PagedResult{T}.Page"/> says which. The page size is
    /// clamped to 1 through <see cref="PeopleListQuery.MaxPageSize"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="PeopleListQuery.Sort"/> is not a defined <see cref="PeopleSort"/>. Thrown before
    /// anything is read.
    /// </exception>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<PeopleListResult> ListPageAsync(PeopleListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new person owned by the current user. Text is trimmed and blank optional text is
    /// stored as nothing. Throws <see cref="PersonValidationException"/> (nothing is saved) when
    /// <paramref name="request"/> breaks a rule in <see cref="PersonProfileRules"/>, and
    /// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/> if it references a
    /// relationship type or tag id that does not belong to the current user.
    /// </summary>
    Task<Person> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the person with <paramref name="personId"/>. Returns <see langword="false"/> when
    /// it does not exist or does not belong to the current user (indistinguishable to the
    /// caller); returns <see langword="true"/> on success. Every profile field is replaced. Throws
    /// <see cref="PersonValidationException"/> when <paramref name="request"/> breaks a rule in
    /// <see cref="PersonProfileRules"/>, and
    /// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/> if it references a
    /// relationship type or tag id that does not belong to the current user - in both cases
    /// nothing is saved. A person that does not exist or is not the current user's returns
    /// <see langword="false"/> before any of that is checked.
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
