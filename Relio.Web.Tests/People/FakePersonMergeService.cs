using Relio.Application.People;
using Relio.Domain;

namespace Relio.Web.Tests.People;

/// <summary>
/// An in-memory <see cref="IPersonMergeService"/> for component tests: returns <see cref="Candidates"/>,
/// records every merge, and can be told to report "gone" or to throw. A successful merge removes the
/// duplicate from the shared <see cref="FakePeopleService.Known"/> list, like the real service.
/// </summary>
internal sealed class FakePersonMergeService(FakePeopleService? people = null) : IPersonMergeService
{
    /// <summary>What <see cref="ListCandidatesAsync"/> returns; null means the person is not found.</summary>
    public MergeCandidates? Candidates { get; set; } = new([], []);

    /// <summary>The ids <see cref="ListCandidatesAsync"/> was called with, in order.</summary>
    public List<Guid> CandidateCalls { get; } = [];

    /// <summary>Every merge request, in order.</summary>
    public List<MergePeopleRequest> Merges { get; } = [];

    /// <summary>What <see cref="MergeAsync"/> returns.</summary>
    public MergeOutcome Outcome { get; set; } = MergeOutcome.Merged;

    /// <summary>Thrown by (and then cleared from) the next <see cref="MergeAsync"/> call.</summary>
    public Exception? ThrowOnNextMerge { get; set; }

    public Task<MergeCandidates?> ListCandidatesAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        CandidateCalls.Add(personId);
        return Task.FromResult(Candidates);
    }

    public Task<MergeOutcome> MergeAsync(MergePeopleRequest request, CancellationToken cancellationToken = default)
    {
        Merges.Add(request);
        if (ThrowOnNextMerge is { } exception)
        {
            ThrowOnNextMerge = null;
            throw exception;
        }

        if (Outcome == MergeOutcome.Merged)
        {
            people?.Known.RemoveAll(p => p.Id == request.DuplicateId);
        }

        return Task.FromResult(Outcome);
    }
}
