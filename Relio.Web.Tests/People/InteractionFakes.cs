using Relio.Application.Interactions;
using Relio.Application.Timeline;
using Relio.Domain;

namespace Relio.Web.Tests.People;

internal sealed class FakeInteractionService : IInteractionService
{
    public List<InteractionDetails> Known { get; } = [];

    public List<CreateInteractionRequest> Created { get; } = [];

    public List<(Guid InteractionId, UpdateInteractionRequest Request)> Updated { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public List<(Guid ProfilePersonId, IReadOnlyCollection<Guid>? IncludedParticipantIds)> CandidateQueries { get; } = [];

    public IReadOnlyList<InteractionParticipantOption> CandidateOptions { get; set; } = [];

    public Guid NextId { get; set; } = Guid.NewGuid();

    public bool UpdateResult { get; set; } = true;

    public bool DeleteResult { get; set; } = true;

    public Exception? ThrowOnNextCreate { get; set; }

    public Exception? ThrowOnNextUpdate { get; set; }

    public Task<InteractionDetails?> GetAsync(Guid interactionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Known.FirstOrDefault(interaction => interaction.Id == interactionId));

    public Task<IReadOnlyList<InteractionParticipantOption>> ListParticipantCandidatesAsync(
        Guid profilePersonId,
        IReadOnlyCollection<Guid>? includedParticipantIds = null,
        CancellationToken cancellationToken = default)
    {
        CandidateQueries.Add((profilePersonId, includedParticipantIds));
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
        return Task.FromResult(NextId);
    }

    public Task<bool> UpdateAsync(
        Guid interactionId,
        UpdateInteractionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextUpdate is { } exception)
        {
            ThrowOnNextUpdate = null;
            throw exception;
        }

        Updated.Add((interactionId, request));
        return Task.FromResult(UpdateResult);
    }

    public Task<bool> DeleteAsync(Guid interactionId, CancellationToken cancellationToken = default)
    {
        Deleted.Add(interactionId);
        return Task.FromResult(DeleteResult);
    }
}

internal sealed class FakePersonTimelineService : IPersonTimelineService
{
    public List<(Guid PersonId, TimelineFilter Filter, TimelineContinuation? Continuation, int PageSize)> Queries { get; } = [];

    public Func<Guid, TimelineFilter, TimelineContinuation?, int, PersonTimelinePage?>? Result { get; set; }

    public Task<PersonTimelinePage?> GetPageAsync(
        Guid personId,
        TimelineFilter filter = TimelineFilter.All,
        TimelineContinuation? continuation = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);
        Queries.Add((personId, filter, continuation, boundedPageSize));
        if (Result is { } result)
        {
            return Task.FromResult(result(personId, filter, continuation, boundedPageSize));
        }

        return Task.FromResult<PersonTimelinePage?>(
            new PersonTimelinePage([], boundedPageSize, false, null));
    }
}
