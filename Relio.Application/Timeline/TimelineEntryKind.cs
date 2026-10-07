namespace Relio.Application.Timeline;

/// <summary>The concrete kind of an entry in a person's timeline.</summary>
public enum TimelineEntryKind
{
    /// <summary>A shared, multi-person interaction.</summary>
    Interaction,

    /// <summary>A private note for one person.</summary>
    Note,

    /// <summary>A difficult moment. This entry type is reserved for the later feature.</summary>
    DifficultMoment,
}
