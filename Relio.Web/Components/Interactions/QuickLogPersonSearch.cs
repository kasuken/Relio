using Relio.Application.Interactions;
using Relio.Application.People;

namespace Relio.Web.Components.Interactions;

/// <summary>Filters the quick-log person picker by normalized display name, entirely in memory.</summary>
/// <remarks>Names and search text stay in the component; never log them or put them in a URL.</remarks>
public static class QuickLogPersonSearch
{
    /// <summary>The maximum number of people shown in the picker at once.</summary>
    public const int DefaultMax = 20;

    /// <summary>
    /// Returns active people whose normalized display name contains <paramref name="text"/>,
    /// putting prefix matches first. With no text, returns the first <paramref name="max"/> people.
    /// </summary>
    /// <param name="people">The current user's candidate people.</param>
    /// <param name="text">The text entered in the picker.</param>
    /// <param name="max">The maximum number of options to return.</param>
    /// <returns>At most <paramref name="max"/> active people in their original order within each match group.</returns>
    public static IReadOnlyList<InteractionParticipantOption> Filter(
        IReadOnlyList<InteractionParticipantOption> people,
        string? text,
        int max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentOutOfRangeException.ThrowIfNegative(max);

        var wanted = PersonNameNormalizer.Normalize(text);
        if (wanted.Length == 0)
        {
            return people
                .Where(person => !person.IsArchived)
                .Take(max)
                .ToArray();
        }

        var starts = new List<InteractionParticipantOption>();
        var contains = new List<InteractionParticipantOption>();
        foreach (var person in people)
        {
            if (person.IsArchived)
            {
                continue;
            }

            var name = PersonNameNormalizer.Normalize(person.DisplayName);
            if (name.StartsWith(wanted, StringComparison.Ordinal))
            {
                starts.Add(person);
            }
            else if (name.Contains(wanted, StringComparison.Ordinal))
            {
                contains.Add(person);
            }
        }

        return starts.Concat(contains).Take(max).ToArray();
    }
}
