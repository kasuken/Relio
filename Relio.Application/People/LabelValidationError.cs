namespace Relio.Application.People;

/// <summary>
/// Why a relationship type or tag name was refused when managing them in settings (issue #25).
/// Codes only: <c>Relio.Web</c> words each one next to the field it belongs to.
/// </summary>
public enum LabelValidationError
{
    /// <summary>The name is empty or only whitespace.</summary>
    NameRequired,

    /// <summary>The name, after trimming, is longer than the label allows.</summary>
    NameTooLong,

    /// <summary>The user already has a label with that name (compared case-insensitively).</summary>
    NameTaken,
}
