namespace Relio.Application.People;

/// <summary>
/// Decides which existing people might be the person being entered. Pure and synchronous: the data
/// layer loads <see cref="DuplicateCandidate"/> projections, this class does the matching, so the
/// rules are testable with no database. Issue #27 (the person form), #28 (merge) and #29 (import)
/// share it; #29 calls <see cref="Prepare"/> once and then <see cref="Find(DuplicateProbe, PreparedCandidates, Guid?)"/>
/// per row instead of loading the people again for every row.
/// </summary>
/// <remarks>
/// <para>
/// A candidate is a possible duplicate when any of these holds (reasons are reported per person and
/// several can apply):
/// </para>
/// <list type="bullet">
/// <item><description><b>Same name</b>: the normalized full names are equal (<see cref="PersonNameNormalizer"/>).</description></item>
/// <item><description><b>Similar name</b>, not the same: the last names are equal (two missing last names count as equal) and the first names are close
/// (<see cref="NameSimilarity.FirstNamesClose"/>: a small typo, or one is the start of the other); or a nickname equals the other's first name or nickname while the last names are
/// compatible (equal or one missing); or the first names are equal and the last names are close
/// (<see cref="NameSimilarity.LastNamesClose"/>) or only one side has a last name.</description></item>
/// <item><description><b>Same email</b>: an email key (lower-cased) is equal.</description></item>
/// <item><description><b>Same phone</b>: a phone key is equal, or both have at least eight digits and end in the same eight.</description></item>
/// </list>
/// <para>
/// Results are ranked (same name, same email and same phone outrank a similar name; active people
/// before archived ones; then by name) and capped at <see cref="MaxResults"/>. There is no built-in
/// nickname dictionary (Bob and Robert): only a stored nickname counts.
/// </para>
/// <para>
/// <b>Personal data.</b> Everything here is computed in memory and thrown away. Nothing is stored,
/// cached or logged.
/// </para>
/// </remarks>
public static class PossibleDuplicateMatcher
{
    /// <summary>The most people one check reports; it keeps the warning short.</summary>
    public const int MaxResults = 5;

    /// <summary>How many final digits two long phone numbers must share to match without a country code.</summary>
    public const int PhoneSuffixLength = 8;

    private const int SameNameScore = 4;
    private const int ContactScore = 3;
    private const int SimilarNameScore = 2;

    /// <summary>Normalizes every candidate once, so many probes can be matched against the same set.</summary>
    public static PreparedCandidates Prepare(IEnumerable<DuplicateCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return new PreparedCandidates(candidates
            .Select(candidate => new PreparedCandidate(
                candidate,
                PersonNameNormalizer.Normalize(candidate.FirstName),
                PersonNameNormalizer.Normalize(candidate.LastName),
                PersonNameNormalizer.Normalize(candidate.Nickname),
                PersonNameNormalizer.FullName(candidate.FirstName, candidate.LastName)))
            .ToList());
    }

    /// <summary>Matches <paramref name="probe"/> against <paramref name="candidates"/> (prepared here, once).</summary>
    public static IReadOnlyList<PossibleDuplicate> Find(DuplicateProbe probe, IEnumerable<DuplicateCandidate> candidates, Guid? excludePersonId = null) =>
        Find(probe, Prepare(candidates), excludePersonId);

    /// <summary>
    /// Matches <paramref name="probe"/> against already prepared candidates and returns at most
    /// <see cref="MaxResults"/> people, strongest first. <paramref name="excludePersonId"/> is skipped.
    /// </summary>
    public static IReadOnlyList<PossibleDuplicate> Find(DuplicateProbe probe, PreparedCandidates candidates, Guid? excludePersonId = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(candidates);

        var matches = new List<(PreparedCandidate Candidate, List<PossibleDuplicateReason> Reasons, int Score)>();

        foreach (var prepared in candidates.Items)
        {
            if (prepared.Source.Id == excludePersonId)
            {
                continue;
            }

            var reasons = new List<PossibleDuplicateReason>(4);
            var score = 0;

            if (IsSameName(probe, prepared))
            {
                reasons.Add(PossibleDuplicateReason.SameName);
                score += SameNameScore;
            }
            else if (IsSimilarName(probe, prepared))
            {
                reasons.Add(PossibleDuplicateReason.SimilarName);
                score += SimilarNameScore;
            }

            if (SharesEmail(probe, prepared.Source))
            {
                reasons.Add(PossibleDuplicateReason.SameEmail);
                score += ContactScore;
            }

            if (SharesPhone(probe, prepared.Source))
            {
                reasons.Add(PossibleDuplicateReason.SamePhone);
                score += ContactScore;
            }

            if (reasons.Count > 0)
            {
                matches.Add((prepared, reasons, score));
            }
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Candidate.Source.IsArchived)
            .ThenBy(match => match.Candidate.FullName, StringComparer.Ordinal)
            .ThenBy(match => match.Candidate.Source.Id)
            .Take(MaxResults)
            .Select(match => new PossibleDuplicate(
                match.Candidate.Source.Id,
                match.Candidate.Source.FirstName,
                match.Candidate.Source.LastName,
                match.Candidate.Source.IsArchived,
                match.Reasons.Order().ToList()))
            .ToList();
    }

    /// <summary>The number of ASCII digits in a phone key (the leading <c>+</c> is not counted).</summary>
    internal static int DigitCount(string phoneKey)
    {
        var count = 0;
        foreach (var character in phoneKey)
        {
            if (character is >= '0' and <= '9')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The last <see cref="PhoneSuffixLength"/> digits of a phone key with at least that many digits; otherwise the whole key.</summary>
    internal static string PhoneSuffix(string phoneKey) =>
        DigitCount(phoneKey) >= PhoneSuffixLength ? phoneKey[^PhoneSuffixLength..] : phoneKey;

    private static bool IsSameName(DuplicateProbe probe, PreparedCandidate candidate) =>
        probe.First.Length > 0 && string.Equals(probe.FullName, candidate.FullName, StringComparison.Ordinal);

    private static bool IsSimilarName(DuplicateProbe probe, PreparedCandidate candidate)
    {
        if (probe.First.Length == 0)
        {
            return false;
        }

        var sameLast = string.Equals(probe.Last, candidate.Last, StringComparison.Ordinal);

        // (a) The same last name (or none on either side) and a close first name: Jon and John Smith.
        if (sameLast && NameSimilarity.FirstNamesClose(probe.First, candidate.First))
        {
            return true;
        }

        // (b) A nickname that is the other's first name, or both nicknames equal, with compatible last names.
        var compatibleLast = sameLast || probe.Last.Length == 0 || candidate.Last.Length == 0;
        if (compatibleLast
            && ((probe.Nickname.Length > 0 && probe.Nickname == candidate.First)
                || (candidate.Nickname.Length > 0 && candidate.Nickname == probe.First)
                || (probe.Nickname.Length > 0 && probe.Nickname == candidate.Nickname)))
        {
            return true;
        }

        if (!string.Equals(probe.First, candidate.First, StringComparison.Ordinal))
        {
            return false;
        }

        // (c) The same first name and a last name one typo away; (d) the same first name and one side has no last name.
        return probe.Last.Length > 0 && candidate.Last.Length > 0
            ? NameSimilarity.LastNamesClose(probe.Last, candidate.Last)
            : probe.Last.Length != candidate.Last.Length;
    }

    private static bool SharesEmail(DuplicateProbe probe, DuplicateCandidate candidate)
    {
        if (probe.EmailKeys.Count == 0 || candidate.EmailKeys.Count == 0)
        {
            return false;
        }

        foreach (var key in probe.EmailKeys)
        {
            foreach (var other in candidate.EmailKeys)
            {
                if (string.Equals(key, other, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SharesPhone(DuplicateProbe probe, DuplicateCandidate candidate)
    {
        if (probe.PhoneKeys.Count == 0 || candidate.PhoneKeys.Count == 0)
        {
            return false;
        }

        foreach (var key in probe.PhoneKeys)
        {
            foreach (var other in candidate.PhoneKeys)
            {
                if (string.Equals(key, other, StringComparison.Ordinal)
                    || (DigitCount(key) >= PhoneSuffixLength
                        && DigitCount(other) >= PhoneSuffixLength
                        && string.Equals(PhoneSuffix(key), PhoneSuffix(other), StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// A set of <see cref="DuplicateCandidate"/>s with their names already normalized, built by
/// <see cref="PossibleDuplicateMatcher.Prepare"/> and reused for many probes. In memory only.
/// </summary>
public sealed class PreparedCandidates
{
    internal PreparedCandidates(IReadOnlyList<PreparedCandidate> items) => Items = items;

    /// <summary>How many candidates the set holds.</summary>
    public int Count => Items.Count;

    internal IReadOnlyList<PreparedCandidate> Items { get; }
}

internal sealed record PreparedCandidate(DuplicateCandidate Source, string First, string Last, string Nickname, string FullName);
