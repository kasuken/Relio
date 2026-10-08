namespace Relio.Application.DifficultMoments;

/// <summary>
/// Service for managing user-scoped difficult moments and reflection.
/// </summary>
public interface IDifficultMomentService
{
    /// <summary>Gets a single difficult moment by id, or null if not found or not owned.</summary>
    Task<DifficultMomentDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists difficult moments for a specific person.</summary>
    Task<IReadOnlyList<DifficultMomentSummary>> ListForPersonAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Lists difficult moments across people for reflection overview.</summary>
    Task<IReadOnlyList<DifficultMomentOverviewItem>> ListOverviewAsync(DifficultMomentFilterRequest filter, CancellationToken cancellationToken = default);

    /// <summary>Lists candidate earlier moments for recurrence linking for a person.</summary>
    Task<IReadOnlyList<DifficultMomentLookupItem>> ListCandidatesForRecurrenceAsync(Guid personId, Guid? excludeMomentId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a new difficult moment.</summary>
    Task<DifficultMomentDetails> CreateAsync(CreateDifficultMomentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing difficult moment. Returns false if not found or not owned.</summary>
    Task<bool> UpdateAsync(Guid id, UpdateDifficultMomentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a difficult moment. Returns false if not found or not owned.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
