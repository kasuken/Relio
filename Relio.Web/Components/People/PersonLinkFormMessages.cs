using Relio.Application.People;

namespace Relio.Web.Components.People;

/// <summary>Maps connection validation codes to calm, clear UI messages.</summary>
public static class PersonLinkFormMessages
{
    /// <summary>Returns a user-facing message for every link validation code.</summary>
    public static string Message(PersonLinkValidationError error) => error switch
    {
        PersonLinkValidationError.PersonRequired => "Choose a person.",
        PersonLinkValidationError.SelfLink => "A person can't be connected to themselves.",
        PersonLinkValidationError.KindRequired => "Choose how they are related.",
        PersonLinkValidationError.KindInvalid => "Choose how they are related.",
        PersonLinkValidationError.ArchivedPersonNotAllowed => "Archived people can't be connected. Restore them first.",
        PersonLinkValidationError.AlreadyLinked => "These two people are already connected. Edit the existing connection instead.",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown person link validation code."),
    };

    /// <summary>Whether the code belongs next to the person picker (rather than the kind select).</summary>
    public static bool BelongsToPerson(PersonLinkValidationError error) =>
        error is PersonLinkValidationError.PersonRequired or PersonLinkValidationError.SelfLink;

    /// <summary>Whether the code belongs next to the kind select.</summary>
    public static bool BelongsToKind(PersonLinkValidationError error) =>
        error is PersonLinkValidationError.KindRequired or PersonLinkValidationError.KindInvalid;
}
