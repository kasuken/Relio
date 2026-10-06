namespace Relio.Domain;

/// <summary>
/// What sort of contact detail a <see cref="ContactMethod"/> is. Stored by <b>name</b> (a string
/// column), not by number, so the database is readable and re-ordering the members is harmless.
/// </summary>
/// <remarks>
/// Renaming a member, or adding one, needs a migration that updates the
/// <c>CK_ContactMethods_Kind</c> check constraint (see <c>ContactMethodConfiguration</c>), which
/// is what stops a bug in a service from writing a kind nothing can display.
/// </remarks>
public enum ContactMethodKind
{
    /// <summary>An email address.</summary>
    Email,

    /// <summary>A phone number.</summary>
    Phone,

    /// <summary>A postal address; may span several lines.</summary>
    Address,

    /// <summary>A social media handle or profile link.</summary>
    Social,

    /// <summary>Anything else: a messaging handle, a fax number, a note about how to reach them.</summary>
    Other,
}
