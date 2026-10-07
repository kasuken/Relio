namespace Relio.Application.People.Import;

/// <summary>
/// One row of the import preview: a person as it would be created, with what was found wrong or
/// similar. Personal data: lives in the page for the length of the import and is never persisted or logged.
/// </summary>
/// <param name="RowNumber">The card or record number in the file.</param>
/// <param name="RowId">A new id for this row, used only to match rows within the file and as a render key. Never a person's id.</param>
/// <param name="DisplayName">The name to show; empty when the file gave none.</param>
/// <param name="Request">What would be created, or <see langword="null"/> when a blocking problem stops the row.</param>
/// <param name="Problems">What was wrong, blocking problems first.</param>
/// <param name="ExistingMatches">The user's own people this row may be.</param>
/// <param name="SameAsRowNumber">The earlier row of this file this row may repeat, or <see langword="null"/>.</param>
public sealed record ImportCandidate(
    int RowNumber,
    Guid RowId,
    string DisplayName,
    ImportPersonRequest? Request,
    IReadOnlyList<ImportProblem> Problems,
    IReadOnlyList<PossibleDuplicate> ExistingMatches,
    int? SameAsRowNumber)
{
    /// <summary>Whether the row can be imported at all.</summary>
    public bool CanImport => Request is not null;

    /// <summary>Whether the row starts selected: importable, and not a possible duplicate of anything.</summary>
    public bool SelectedByDefault => CanImport && ExistingMatches.Count == 0 && SameAsRowNumber is null;

    /// <summary>Whether the row has a problem or a possible duplicate worth a look.</summary>
    public bool NeedsAttention => Problems.Count > 0 || ExistingMatches.Count > 0 || SameAsRowNumber is not null;
}

/// <summary>What a file would import, for the user to confirm. Nothing here is saved.</summary>
/// <param name="Candidates">One row per person found, in file order.</param>
/// <param name="TotalFound">How many people the file held (more than <paramref name="Candidates"/> holds when truncated).</param>
/// <param name="Truncated">Whether the file held more than <see cref="ImportLimits.MaxPeople"/> people.</param>
/// <param name="SkippedEmpty">How many cards or records held nothing and were skipped.</param>
public sealed record ImportPreview(IReadOnlyList<ImportCandidate> Candidates, int TotalFound, bool Truncated, int SkippedEmpty)
{
    /// <summary>How many rows can be imported.</summary>
    public int ImportableCount => Candidates.Count(candidate => candidate.CanImport);

    /// <summary>How many rows a blocking problem stops.</summary>
    public int BlockedCount => Candidates.Count(candidate => !candidate.CanImport);

    /// <summary>How many rows may be someone already in the list, or a repeat of an earlier row.</summary>
    public int PossibleDuplicateCount => Candidates.Count(candidate => candidate.ExistingMatches.Count > 0 || candidate.SameAsRowNumber is not null);
}
