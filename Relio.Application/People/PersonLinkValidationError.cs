namespace Relio.Application.People;

/// <summary>Validation error codes for linking two people. Relio.Web words each one.</summary>
public enum PersonLinkValidationError
{
    /// <summary>No other person was chosen.</summary>
    PersonRequired,

    /// <summary>A person cannot be linked to themselves.</summary>
    SelfLink,

    /// <summary>No kind was chosen.</summary>
    KindRequired,

    /// <summary>The kind is not one of the defined kinds.</summary>
    KindInvalid,

    /// <summary>A new link needs both people to be active; an archived person must be restored first.</summary>
    ArchivedPersonNotAllowed,

    /// <summary>The two people are already linked.</summary>
    AlreadyLinked,
}
