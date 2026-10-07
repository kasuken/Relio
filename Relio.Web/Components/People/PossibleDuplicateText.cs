using Relio.Application.People;

namespace Relio.Web.Components.People;

/// <summary>
/// The words of the possible-duplicate warning (issue #27), in Relio's voice: sentence case, calm,
/// no exclamation marks, "you" never "I". Kept out of the component so they are unit tested without
/// rendering. The warning is a nudge, never a blocker: it always offers "Save anyway".
/// </summary>
public static class PossibleDuplicateText
{
    /// <summary>The warning's heading.</summary>
    public const string Title = "You may already have this person.";

    /// <summary>The hint read out after a link, because it opens a new tab.</summary>
    public const string NewTabHint = "(opens in a new tab)";

    /// <summary>The line under the heading, for <paramref name="count"/> matching profiles.</summary>
    public static string Intro(int count) => count == 1
        ? "This profile looks similar. Open it to check, or save anyway."
        : "These profiles look similar. Open them to check, or save anyway.";

    /// <summary>The wording of one reason.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not a defined value.</exception>
    public static string Reason(PossibleDuplicateReason reason) => reason switch
    {
        PossibleDuplicateReason.SameName => "Same name",
        PossibleDuplicateReason.SimilarName => "Similar name",
        PossibleDuplicateReason.SameEmail => "Same email address",
        PossibleDuplicateReason.SamePhone => "Same phone number",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a known PossibleDuplicateReason."),
    };

    /// <summary>The line under a match's name: its reasons, then "Archived" for an archived person, joined by a middle dot.</summary>
    public static string Describe(PossibleDuplicate match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var parts = match.Reasons.Select(Reason).ToList();
        if (match.IsArchived)
        {
            parts.Add("Archived");
        }

        return string.Join(" · ", parts);
    }
}
