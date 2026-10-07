namespace Relio.Application.People;

/// <summary>
/// Merges two of the current user's profiles of the same person into one (issue #28). Every method
/// reads and writes only the signed-in user's data; a person that is missing and one that belongs to
/// someone else are reported the same way.
/// </summary>
public interface IPersonMergeService
{
    /// <summary>
    /// The people <paramref name="personId"/> can be merged with: the possible duplicates first (the
    /// same matcher as the duplicate warning), then everyone else, archived people included.
    /// </summary>
    /// <returns><see langword="null"/> when the person is missing or not the current user's.</returns>
    /// <exception cref="Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<MergeCandidates?> ListCandidatesAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges <see cref="MergePeopleRequest.DuplicateId"/> into <see cref="MergePeopleRequest.PrimaryId"/>:
    /// every contact method, tag and (as later features arrive) interaction, note, reminder and
    /// difficult moment moves to the primary, the fields follow
    /// <see cref="PersonMergeRules.Combine"/>, and the duplicate is removed - all in one save, so it
    /// either happens completely or not at all. It cannot be undone.
    /// </summary>
    /// <returns>
    /// <see cref="MergeOutcome.Merged"/>, or <see cref="MergeOutcome.NotFound"/> when either person is
    /// missing or not the current user's (nothing changes, and the two cases look the same), or when one
    /// of them was removed while the merge ran.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Both ids are the same, or a field choice is malformed. Raised before any database work.
    /// </exception>
    /// <exception cref="PersonValidationException">
    /// The merged profile would break a rule, for instance more than 20 contact methods or tags
    /// together, or two texts that are too long to keep both. Nothing was saved.
    /// </exception>
    /// <exception cref="Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<MergeOutcome> MergeAsync(MergePeopleRequest request, CancellationToken cancellationToken = default);
}
