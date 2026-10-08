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
    /// tags (ordered by name), its <see cref="Person.ContactMethods"/> (ordered by
    /// <see cref="ContactMethod.SortOrder"/>) and <see cref="Person.RelationshipType"/> loaded, so a
    /// later read always sees what is in the database now.
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
    Task<PeopleListResult> ListPageAsync(PeopleListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the current user's people by first name, last name or nickname.
    /// Excludes archived people by default unless <paramref name="includeArchived"/> is <see langword="true"/>.
    /// Returns matching results ordered by name, up to <paramref name="limit"/> entries.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<IReadOnlyList<PersonSearchResult>> SearchAsync(string query, int limit = 10, bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds up to <see cref="PossibleDuplicateMatcher.MaxResults"/> of the current user's people who
    /// might be the person described by <paramref name="query"/>: the same or a similar name, the
    /// same email address, or the same phone number (the rules are in
    /// <see cref="PossibleDuplicateMatcher"/>), strongest first. Only the current user's people are
    /// ever compared, and <b>archived people are included</b> (flagged by
    /// <see cref="PossibleDuplicate.IsArchived"/>): an archived duplicate is still a duplicate.
    /// <see cref="PossibleDuplicateQuery.ExcludePersonId"/> leaves one person out. A blank first name
    /// means only the email and phone rules run, and a query with nothing to compare returns nothing.
    /// Reads only: nothing is saved, cached or logged, so the answer always reflects the database now.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<IReadOnlyList<PossibleDuplicate>> FindPossibleDuplicatesAsync(PossibleDuplicateQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new person owned by the current user, with the contact methods and tags in the
    /// request. Text is trimmed and blank optional text is stored as nothing. Throws
    /// <see cref="PersonValidationException"/> (nothing is saved) when <paramref name="request"/>
    /// breaks a rule in <see cref="PersonProfileRules"/> or <see cref="ContactMethodRules"/>, and
    /// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/> (naming which kind
    /// in <c>EntityName</c>) if it references a relationship type or tag id that does not belong to
    /// the current user, or carries a contact method id (a new person has none to edit). A tag name
    /// that matches none of the user's tags creates a tag in the same save.
    /// </summary>
    /// <exception cref="ArgumentException">Two contact methods in the request share an id.</exception>
    Task<Person> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the person with <paramref name="personId"/>. Returns <see langword="false"/> when
    /// it does not exist or does not belong to the current user (indistinguishable to the
    /// caller); returns <see langword="true"/> on success. Every profile field is replaced, the tag
    /// set is replaced (tags named in <see cref="UpdatePersonRequest.NewTagNames"/> are matched
    /// ignoring case and created when new), and the contact methods are diffed by id: matched
    /// ones are edited, ones missing from the request are deleted, ones without an id are added.
    /// Throws <see cref="PersonValidationException"/> when <paramref name="request"/> breaks a rule
    /// in <see cref="PersonProfileRules"/> or <see cref="ContactMethodRules"/>, and
    /// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/> (naming which kind
    /// in <c>EntityName</c>) if it references a relationship type, tag or contact method that does
    /// not belong to the current user - in both cases nothing is saved. A person that does not
    /// exist or is not the current user's returns <see langword="false"/> before any of that is
    /// checked.
    /// </summary>
    /// <remarks>
    /// Last write wins: there is no concurrency token, and a save from a stale tab replaces what is
    /// there (it only notices a contact method that was deleted meanwhile, by its id).
    /// <see cref="Person.UpdatedAtUtc"/> is not bumped when only tags or contact methods change.
    /// </remarks>
    /// <exception cref="ArgumentException">Two contact methods in the request share an id.</exception>
    Task<bool> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives the person with <paramref name="personId"/>: hidden from the people list by default,
    /// reversible with <see cref="RestoreAsync"/>. Returns <see langword="false"/> when it does not
    /// exist or does not belong to the current user (indistinguishable to the caller);
    /// <see langword="true"/> on success. Archiving an already-archived person is a no-op that still
    /// returns <see langword="true"/> and keeps the original <see cref="Person.ArchivedAtUtc"/>.
    /// Never touches the person's contact methods, tags or (later) interactions, notes, reminders
    /// and difficult moments: archiving hides, it does not remove.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<bool> ArchiveAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a previously archived person, with everything that was recorded about them. Returns
    /// <see langword="false"/> when it does not exist or does not belong to the current user
    /// (indistinguishable to the caller); <see langword="true"/> on success. Restoring a person who
    /// is not archived is a no-op that still returns <see langword="true"/> and changes nothing, not
    /// even <see cref="OwnedEntity.UpdatedAtUtc"/>.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<bool> RestoreAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes the person with <paramref name="personId"/>, active or archived, and
    /// everything that belongs to them - their contact methods and tag links today; later their
    /// interactions, notes, reminders and difficult moments - in one save: either all of it goes or
    /// none of it. The user's tags and relationship types are kept (only this person's links to the
    /// tags go), and so are other people's links to the same tags. There is no undo and no soft
    /// delete: archive instead when the person might matter again. Returns <see langword="false"/>
    /// and deletes nothing when the person does not exist or does not belong to the current user
    /// (indistinguishable to the caller, so deleting twice is <see langword="false"/> the second
    /// time); <see langword="true"/> on success.
    /// </summary>
    /// <remarks>
    /// Backups are outside the app's control and keep a deleted person until they expire (issue #65).
    /// Two deletes of the same person racing from two tabs: the loser normally reads
    /// <see langword="false"/>; in the tiny window between both loading the person and the first
    /// save, the second save throws a <c>DbUpdateConcurrencyException</c> (nothing is half-deleted)
    /// and the caller may treat it as "already gone".
    /// </remarks>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<bool> DeleteAsync(Guid personId, CancellationToken cancellationToken = default);
}
