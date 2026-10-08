using Relio.Application.DifficultMoments;
using Relio.Domain;

namespace Relio.Web.Tests.Shared;

public sealed class FakeDifficultMomentService : IDifficultMomentService
{
    public List<CreateDifficultMomentRequest> Created { get; } = [];
    public List<(Guid Id, UpdateDifficultMomentRequest Request)> Updated { get; } = [];
    public List<Guid> Deleted { get; } = [];

    public DifficultMomentDetails? MomentToReturn { get; set; }
    public IReadOnlyList<DifficultMomentSummary> SummariesToReturn { get; set; } = [];
    public IReadOnlyList<DifficultMomentOverviewItem> OverviewItemsToReturn { get; set; } = [];
    public IReadOnlyList<DifficultMomentLookupItem> CandidatesToReturn { get; set; } = [];

    public DifficultMomentFilterRequest? LastOverviewFilter { get; private set; }

    public Exception? ThrowOnCreate { get; set; }
    public Exception? ThrowOnUpdate { get; set; }
    public Exception? ThrowOnGet { get; set; }
    public Exception? ThrowOnDelete { get; set; }

    public Task<DifficultMomentDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (ThrowOnGet is not null) throw ThrowOnGet;
        return Task.FromResult(MomentToReturn);
    }

    public Task<IReadOnlyList<DifficultMomentSummary>> ListForPersonAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SummariesToReturn);
    }

    public Task<IReadOnlyList<DifficultMomentOverviewItem>> ListOverviewAsync(DifficultMomentFilterRequest filter, CancellationToken cancellationToken = default)
    {
        LastOverviewFilter = filter;
        return Task.FromResult(OverviewItemsToReturn);
    }

    public Task<IReadOnlyList<DifficultMomentLookupItem>> ListCandidatesForRecurrenceAsync(Guid personId, Guid? excludeMomentId = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CandidatesToReturn);
    }

    public Task<DifficultMomentDetails> CreateAsync(CreateDifficultMomentRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnCreate is not null) throw ThrowOnCreate;
        Created.Add(request);
        var details = MomentToReturn ?? new DifficultMomentDetails(
            Guid.NewGuid(),
            request.PersonId,
            "Person",
            false,
            request.OccurredOn,
            request.Description,
            request.Trigger,
            request.Resolution,
            request.LessonsLearned,
            request.Status,
            request.ResolvedOn,
            request.RecurrenceOfId,
            null,
            [],
            DateTime.UtcNow,
            DateTime.UtcNow);
        return Task.FromResult(details);
    }

    public Task<bool> UpdateAsync(Guid id, UpdateDifficultMomentRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpdate is not null) throw ThrowOnUpdate;
        Updated.Add((id, request));
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (ThrowOnDelete is not null) throw ThrowOnDelete;
        Deleted.Add(id);
        return Task.FromResult(true);
    }
}
