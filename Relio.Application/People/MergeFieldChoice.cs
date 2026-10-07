namespace Relio.Application.People;

/// <summary>
/// Which side's value a merged profile keeps for a <see cref="MergeField"/> that the two profiles
/// disagree on.
/// </summary>
public enum MergeFieldChoice
{
    /// <summary>The profile that is kept (the primary): its value stays.</summary>
    Primary,

    /// <summary>The profile that is merged in and removed (the duplicate): its value replaces the primary's.</summary>
    Duplicate,

    /// <summary>
    /// Both texts, the primary's first, a blank line between them. Valid only for
    /// <see cref="MergeField.HowWeMet"/> and <see cref="MergeField.Details"/>.
    /// </summary>
    Both,
}
