namespace Relio.Application.People;

/// <summary>
/// The people one person can be merged with: the likely duplicates first, then everyone else.
/// </summary>
/// <param name="Suggestions">The possible duplicates of the person, best first (the same matcher as the duplicate warning).</param>
/// <param name="Others">
/// Every other person the user has, archived ones included, sorted by first name, last name and id.
/// Never includes the person themselves.
/// </param>
public sealed record MergeCandidates(
    IReadOnlyList<PossibleDuplicate> Suggestions,
    IReadOnlyList<PersonSummary> Others);
