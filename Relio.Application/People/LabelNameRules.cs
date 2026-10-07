namespace Relio.Application.People;

/// <summary>
/// The rules for the name of a relationship type or a tag typed into settings (issue #25), as pure
/// functions. Both kinds share the tag rules from issue #24 on purpose: one definition of "the same
/// name" for everything a user labels people with. Uniqueness needs the user's other labels, so it
/// is checked in the services (with <see cref="Comparer"/>); the unique <c>(OwnerId, Name)</c>
/// indexes remain the authority for a race.
/// </summary>
public static class LabelNameRules
{
    /// <summary>
    /// Trims a name and collapses every run of whitespace to one space, keeping the casing the user
    /// typed. Blank becomes <see langword="null"/>.
    /// </summary>
    public static string? Normalize(string? name) => TagNameRules.Normalize(name);

    /// <summary>How two names are compared for "the same name" (case-insensitive, accent-sensitive).</summary>
    public static StringComparer Comparer => TagNameRules.Comparer;

    /// <summary>
    /// Checks an already normalized name against the request-only rules.
    /// </summary>
    /// <param name="normalizedName">The result of <see cref="Normalize"/>.</param>
    /// <param name="maxLength">The longest name the label allows.</param>
    /// <returns>The problem, or <see langword="null"/> when the name is acceptable.</returns>
    public static LabelValidationError? Validate(string? normalizedName, int maxLength)
    {
        if (normalizedName is null)
        {
            return LabelValidationError.NameRequired;
        }

        return normalizedName.Length > maxLength ? LabelValidationError.NameTooLong : null;
    }
}
