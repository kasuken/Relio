using Relio.Application.Timeline;

namespace Relio.Web.Components.People;

/// <summary>Plain-language labels for the profile's timeline filter and empty states.</summary>
public static class PersonTimelineText
{
    /// <summary>Returns a filter label.</summary>
    public static string FilterName(TimelineFilter filter) => filter switch
    {
        TimelineFilter.All => "Everything",
        TimelineFilter.Interaction => "Interactions",
        TimelineFilter.Note => "Notes",
        TimelineFilter.DifficultMoment => "Difficult moments",
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown timeline filter."),
    };

    /// <summary>Returns the empty-state message for a filter.</summary>
    public static string EmptyMessage(TimelineFilter filter) => filter switch
    {
        TimelineFilter.All => "Nothing has been recorded yet. Add a note or log an interaction to begin.",
        TimelineFilter.Interaction => "No interactions have been recorded yet.",
        TimelineFilter.Note => "No notes have been recorded yet.",
        TimelineFilter.DifficultMoment => "Difficult moments will appear here when that feature is available.",
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown timeline filter."),
    };
}
