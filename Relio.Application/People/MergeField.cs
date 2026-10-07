namespace Relio.Application.People;

/// <summary>
/// A part of a person's profile that merging has to decide on (issue #28). Contact methods, tags and
/// the last contacted date are not here: they are always combined, never chosen between.
/// </summary>
public enum MergeField
{
    /// <summary>The first and last name, always kept together from one side.</summary>
    Name,

    /// <summary>The nickname.</summary>
    Nickname,

    /// <summary>The relationship type.</summary>
    RelationshipType,

    /// <summary>The birthday: day, month and year, always kept together from one side.</summary>
    Birthday,

    /// <summary>How the user met the person.</summary>
    HowWeMet,

    /// <summary>The free-form details.</summary>
    Details,

    /// <summary>Whether the merged profile is archived (with the time it was archived).</summary>
    ArchivedState,
}
