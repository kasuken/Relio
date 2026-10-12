namespace Relio.Application.People;

/// <summary>
/// Use cases for links between two of the current user's people (parent and child, partners,
/// colleagues, and so on). A link is one record shown on both profiles with the reciprocal
/// wording; it is not the per-person relationship classification (<c>IRelationshipTypeService</c>).
/// </summary>
public interface IPersonLinkService
{
    /// <summary>
    /// Lists a person's connections from that person's side, archived people included, ordered by
    /// the other person's name. A missing or foreign person gives an empty list.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<IReadOnlyList<PersonConnection>> ListForPersonAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links two people and returns the link's id. Both people must belong to the current user and
    /// be active.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    /// <exception cref="Relio.Application.Ownership.ForeignEntityNotOwnedException">
    /// Either person is missing or belongs to another user (the same message for both).
    /// </exception>
    /// <exception cref="PersonLinkValidationException">The request breaks a rule, including linking a pair twice.</exception>
    Task<Guid> CreateAsync(CreatePersonLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the kind of a link, expressed from <see cref="UpdatePersonLinkRequest.PersonId"/>'s
    /// side. Returns false when the link is missing or foreign, or that person is not one of its ends.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    /// <exception cref="PersonLinkValidationException">The kind is missing or not defined.</exception>
    Task<bool> UpdateAsync(Guid linkId, UpdatePersonLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a link from both profiles. The two people stay. Returns false for a missing or
    /// foreign link.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<bool> DeleteAsync(Guid linkId, CancellationToken cancellationToken = default);
}
