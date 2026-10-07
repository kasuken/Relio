namespace Relio.Domain;

/// <summary>The kind of interaction recorded between the current user and a person.</summary>
public enum InteractionKind
{
    /// <summary>A phone or video call.</summary>
    Call,

    /// <summary>Time spent together in person.</summary>
    Meeting,

    /// <summary>A message or written exchange.</summary>
    Message,

    /// <summary>An event attended together.</summary>
    Event,

    /// <summary>An interaction that does not fit another kind.</summary>
    Other,
}
