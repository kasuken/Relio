using Relio.Application.Interactions;
using Relio.Domain;

namespace Relio.Web.Tests.Interactions;

internal sealed class QuickLogInteractionService : IInteractionService
{
    public List<(Guid ProfilePersonId, IReadOnlyCollection<Guid>? IncludedParticipantIds)> CandidateQueries { get; } = [];

    public List<CreateInteractionRequest> Created { get; } = [];

    public IReadOnlyList<InteractionParticipantOption> CandidateOptions { get; set; } = [];

    public Exception? ThrowOnNextCandidateQuery { get; set; }

    public Exception? ThrowOnNextCreate { get; set; }

    public Task<Guid>? CreateTask { get; set; }

    public Task<InteractionDetails?> GetAsync(Guid interactionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InteractionDetails?>(null);

    public Task<IReadOnlyList<InteractionParticipantOption>> ListParticipantCandidatesAsync(
        Guid profilePersonId,
        IReadOnlyCollection<Guid>? includedParticipantIds = null,
        CancellationToken cancellationToken = default)
    {
        CandidateQueries.Add((profilePersonId, includedParticipantIds));
        if (ThrowOnNextCandidateQuery is { } exception)
        {
            ThrowOnNextCandidateQuery = null;
            throw exception;
        }

        return Task.FromResult(CandidateOptions);
    }

    public Task<Guid> CreateAsync(CreateInteractionRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextCreate is { } exception)
        {
            ThrowOnNextCreate = null;
            throw exception;
        }

        Created.Add(request);
        return CreateTask ?? Task.FromResult(Guid.NewGuid());
    }

    public Task<bool> UpdateAsync(
        Guid interactionId,
        UpdateInteractionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> DeleteAsync(Guid interactionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
