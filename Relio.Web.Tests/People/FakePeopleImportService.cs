using Relio.Application.People;
using Relio.Application.People.Import;

namespace Relio.Web.Tests.People;

/// <summary>
/// An in-memory <see cref="IPeopleImportService"/> for component tests: records every preview and every
/// import, builds the preview with the real <see cref="ImportCandidateBuilder"/> (against
/// <see cref="Existing"/>), and can be told to throw once on the next import.
/// </summary>
internal sealed class FakePeopleImportService : IPeopleImportService
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    /// <summary>Every read result <see cref="PreviewAsync"/> was called with, in order.</summary>
    public List<ImportReadResult> Previews { get; } = [];

    /// <summary>Every list <see cref="ImportAsync"/> was called with, in order.</summary>
    public List<IReadOnlyList<ImportPersonRequest>> Imports { get; } = [];

    /// <summary>The user's existing people the preview compares with.</summary>
    public List<DuplicateCandidate> Existing { get; } = [];

    /// <summary>Replaces the preview entirely when set.</summary>
    public Func<ImportReadResult, ImportPreview>? PreviewResult { get; set; }

    /// <summary>Thrown by (and then cleared from) the next <see cref="ImportAsync"/> call.</summary>
    public Exception? ThrowOnNextImport { get; set; }

    public Task<ImportPreview> PreviewAsync(ImportReadResult read, CancellationToken cancellationToken = default)
    {
        Previews.Add(read);
        return Task.FromResult(PreviewResult is { } custom
            ? custom(read)
            : ImportCandidateBuilder.Build(read, Today, PossibleDuplicateMatcher.Prepare(Existing)));
    }

    public Task<int> ImportAsync(IReadOnlyList<ImportPersonRequest> people, CancellationToken cancellationToken = default)
    {
        Imports.Add(people);
        if (ThrowOnNextImport is { } exception)
        {
            ThrowOnNextImport = null;
            throw exception;
        }

        return Task.FromResult(people.Count);
    }
}
