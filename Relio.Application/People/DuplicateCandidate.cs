namespace Relio.Application.People;

/// <summary>
/// One existing person as the matcher sees them: just the names and the comparison keys of their
/// email and phone contact methods (<see cref="ContactMethodRules.ToNormalizedValue"/>). A projection
/// the data layer loads and the matcher normalizes in memory; never persisted or logged.
/// </summary>
public sealed record DuplicateCandidate(
    Guid Id,
    string FirstName,
    string? LastName,
    string? Nickname,
    bool IsArchived,
    IReadOnlyCollection<string> EmailKeys,
    IReadOnlyCollection<string> PhoneKeys);
