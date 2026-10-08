namespace Relio.Domain;

/// <summary>
/// The resolution status of a difficult moment in a relationship.
/// </summary>
public enum DifficultMomentStatus
{
    /// <summary>The difficult moment is open, unresolved or under active reflection.</summary>
    Open,

    /// <summary>The difficult moment has been resolved.</summary>
    Resolved,

    /// <summary>The difficult moment is part of a recurring pattern or issue.</summary>
    Recurring,
}
