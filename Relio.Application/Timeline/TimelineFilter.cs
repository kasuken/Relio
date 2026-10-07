namespace Relio.Application.Timeline;

/// <summary>The entry types shown in a person's timeline.</summary>
public enum TimelineFilter
{
    /// <summary>Interactions and notes, with room for future timeline entries.</summary>
    All,

    /// <summary>Shared interactions only.</summary>
    Interaction,

    /// <summary>Notes only.</summary>
    Note,

    /// <summary>Difficult moments. No difficult-moment entries are stored yet.</summary>
    DifficultMoment,
}
