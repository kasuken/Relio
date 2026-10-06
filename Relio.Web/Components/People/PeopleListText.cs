using System.Globalization;
using Relio.Application.People;
using Relio.Web.Time;

namespace Relio.Web.Components.People;

/// <summary>
/// The words on the people list that are not a person's own: the sort labels, the "last contacted"
/// phrase and the count line. Relio's voice (sentence case, no exclamation marks, "you" never "I"),
/// kept out of the component so it is unit tested without rendering. See the design system's
/// content fundamentals.
/// </summary>
public static class PeopleListText
{
    /// <summary>What a person who has never been contacted shows.</summary>
    public const string NotContactedYet = "Not contacted yet";

    /// <summary>The label of <paramref name="sort"/> in the "Sort by" control.</summary>
    public static string SortLabel(PeopleSort sort) => sort switch
    {
        PeopleSort.Name => "Name",
        PeopleSort.RecentlyAdded => "Recently added",
        PeopleSort.LastContacted => "Last contacted",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Not a known PeopleSort."),
    };

    /// <summary>
    /// "Last contacted today", "Last contacted yesterday", "Last contacted 12 days ago" or, from two
    /// weeks on, "Last contacted 3 March" ("3 March 2025" in another year); "Not contacted yet" when
    /// <paramref name="date"/> is <see langword="null"/>. <paramref name="today"/> is the viewer's
    /// own today (their time zone), never read from the clock here.
    /// </summary>
    public static string LastContacted(DateOnly? date, DateOnly today)
    {
        if (date is not DateOnly contacted)
        {
            return NotContactedYet;
        }

        // The relative phrase starts with a capital only for "Today" and "Yesterday"; a date or a
        // "12 days ago" starts with a digit, so lower-casing the first letter is right for all of them.
        var phrase = DateDisplay.FormatRelative(contacted, today);
        return $"Last contacted {char.ToLowerInvariant(phrase[0])}{phrase[1..]}";
    }

    /// <summary>
    /// The count line above the list. Without archived people shown it counts the active people
    /// ("1 person", "12 people"); with them shown it says how many of the total are archived
    /// ("5 people, 2 archived", "5 people, none archived").
    /// </summary>
    public static string Count(PeopleListResult result, bool includeArchived)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!includeArchived)
        {
            return People(result.ActiveCount);
        }

        var total = result.ActiveCount + result.ArchivedCount;
        var archived = result.ArchivedCount == 0 ? "none" : result.ArchivedCount.ToString("N0", CultureInfo.InvariantCulture);
        return $"{People(total)}, {archived} archived";
    }

    private static string People(int count) =>
        count == 1 ? "1 person" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} people";
}
