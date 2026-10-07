namespace Relio.Application.Interactions;

/// <summary>
/// Use cases for the current user's interactions. Each interaction is one owned record with
/// participant links, so edits and deletes appear on every participant's timeline.
/// </summary>
public interface IInteractionService
{
    /// <summary>
    /// Gets an interaction owned by the current user, including all participant display data, or
    /// returns <see langword="null"/> for a missing or foreign interaction.
    /// </summary>
    Task<InteractionDetails?> GetAsync(Guid interactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists active people that can be added from <paramref name="profilePersonId"/>. That profile
    /// itself is included even when archived, and already-selected archived participants may be
    /// retained while editing; other archived people are excluded from the picker.
    /// </summary>
    Task<IReadOnlyList<InteractionParticipantOption>> ListParticipantCandidatesAsync(
        Guid profilePersonId,
        IReadOnlyCollection<Guid>? includedParticipantIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an interaction and all participant links in one save. Returns its id. Every
    /// participant id must resolve to a person owned by the current user.
    /// </summary>
    /// <exception cref="Relio.Application.Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    /// <exception cref="Relio.Application.Ownership.ForeignEntityNotOwnedException">
    /// One or more participant ids do not belong to the current user.
    /// </exception>
    /// <exception cref="InteractionValidationException">The request breaks an input rule.</exception>
    Task<Guid> CreateAsync(CreateInteractionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an interaction and its complete participant list. Returns false if the interaction
    /// is missing or belongs to another user; returns true after saving the current user's edit.
    /// </summary>
    /// <exception cref="Relio.Application.Ownership.ForeignEntityNotOwnedException">
    /// One or more participant ids do not belong to the current user.
    /// </exception>
    /// <exception cref="InteractionValidationException">The request breaks an input rule.</exception>
    Task<bool> UpdateAsync(Guid interactionId, UpdateInteractionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes an interaction owned by the current user and all its participant links.
    /// Returns false for a missing or foreign interaction.
    /// </summary>
    Task<bool> DeleteAsync(Guid interactionId, CancellationToken cancellationToken = default);
}
