namespace Relio.Application.People;

/// <summary>
/// How close two <b>already normalized</b> names are (see <see cref="PersonNameNormalizer"/>).
/// Pure functions. The thresholds are deliberately stricter than a plain edit distance: a warning
/// that fires on every "Mark" and "Mary" teaches people to ignore it, so short names must match
/// closely and a single-letter substitution in a short name is not a match.
/// </summary>
/// <remarks>Nothing here persists or logs a name.</remarks>
public static class NameSimilarity
{
    /// <summary>The shortest first name that can match a different spelling at all. Shorter names (Al, Ed, two-character CJK names) match only exactly.</summary>
    public const int MinFuzzyFirstNameLength = 3;

    /// <summary>The shortest last name that can match a different spelling.</summary>
    public const int MinFuzzyLastNameLength = 5;

    /// <summary>
    /// The Damerau-Levenshtein distance (optimal string alignment: insertions, deletions,
    /// substitutions and adjacent transpositions each cost 1), or <c><paramref name="max"/> + 1</c>
    /// as soon as it is known to be above <paramref name="max"/>. Works on UTF-16 characters.
    /// </summary>
    public static int Distance(string a, string b, int max)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (Math.Abs(a.Length - b.Length) > max)
        {
            return max + 1;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            var length = Math.Max(a.Length, b.Length);
            return length > max ? max + 1 : length;
        }

        var twoBack = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, twoBack[j - 2] + 1);
                }

                current[j] = value;
                rowMinimum = Math.Min(rowMinimum, value);
            }

            if (rowMinimum > max)
            {
                return max + 1;
            }

            (twoBack, previous, current) = (previous, current, twoBack);
        }

        var distance = previous[b.Length];
        return distance > max ? max + 1 : distance;
    }

    /// <summary>
    /// Whether two normalized first names are probably the same name: equal; or the shorter (at
    /// least three letters) is the start of the longer (Alex and Alexander, John and "john paul");
    /// or a small typo (Jon and John, Jhon and John), except that a single-letter substitution in a
    /// name of five letters or fewer is a different name (Mark and Mary, Jon and Jan, Dan and Don).
    /// Eric and Erik is an accepted miss.
    /// </summary>
    public static bool FirstNamesClose(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        if (shorter.Length < MinFuzzyFirstNameLength)
        {
            return false;
        }

        if (longer.StartsWith(shorter, StringComparison.Ordinal))
        {
            return true;
        }

        var limit = longer.Length <= 8 ? 1 : 2;
        if (Distance(a, b, limit) > limit)
        {
            return false;
        }

        return !(longer.Length <= 5 && a.Length == b.Length && Hamming(a, b) == 1);
    }

    /// <summary>
    /// Whether two normalized last names are probably the same name, used only when the first names
    /// are equal: equal, or both at least five letters and one typo apart (two for names over eight
    /// letters): Smith and Smyth, Johnson and Jonson - but never Hall and Hill or Rossi and Rosi.
    /// </summary>
    public static bool LastNamesClose(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        if (Math.Min(a.Length, b.Length) < MinFuzzyLastNameLength)
        {
            return false;
        }

        var limit = Math.Max(a.Length, b.Length) <= 8 ? 1 : 2;
        return Distance(a, b, limit) <= limit;
    }

    private static int Hamming(string a, string b)
    {
        var differences = 0;
        for (var index = 0; index < a.Length; index++)
        {
            if (a[index] != b[index])
            {
                differences++;
            }
        }

        return differences;
    }
}
