using Relio.Application.People;

namespace Relio.Web.Components.People;

/// <summary>
/// Filters the merge page's list of people as the user types, in memory: the list is loaded once, so
/// typing never queries the database. Names are compared the way duplicates are found
/// (<see cref="PersonNameNormalizer"/>: no case, no accents, single spaces), so "jose" finds "José".
/// Pure.
/// </summary>
/// <remarks>The text the user types is a name: it is only ever compared in memory, never logged or put in an address.</remarks>
public static class MergeCandidateSearch
{
    /// <summary>The most people the picker offers at once.</summary>
    public const int DefaultMax = 20;

    /// <summary>
    /// The people whose name contains <paramref name="text"/>, those whose name <i>starts</i> with it
    /// first, each group in the order given (the list arrives sorted by name). With no text, the first
    /// <paramref name="max"/> people.
    /// </summary>
    public static IReadOnlyList<PersonSummary> Filter(IReadOnlyList<PersonSummary> people, string? text, int max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentOutOfRangeException.ThrowIfNegative(max);

        var wanted = PersonNameNormalizer.Normalize(text);
        if (wanted.Length == 0)
        {
            return people.Take(max).ToList();
        }

        var starts = new List<PersonSummary>();
        var contains = new List<PersonSummary>();
        foreach (var person in people)
        {
            var name = PersonNameNormalizer.FullName(person.FirstName, person.LastName);
            if (name.StartsWith(wanted, StringComparison.Ordinal))
            {
                starts.Add(person);
            }
            else if (name.Contains(wanted, StringComparison.Ordinal))
            {
                contains.Add(person);
            }
        }

        return starts.Concat(contains).Take(max).ToList();
    }
}
