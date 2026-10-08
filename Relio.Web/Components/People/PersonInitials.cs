using System.Globalization;

namespace Relio.Web.Components.People;

/// <summary>
/// The letters shown in a person's monogram avatar: the first character of the first name and,
/// when there is one, of the last name, in upper case. Pure, so it is unit tested without a component.
/// </summary>
public static class PersonInitials
{
    /// <summary>
    /// Builds the monogram, e.g. "AL" for Ada Lovelace and "G" for Grace alone. Works on text
    /// elements, not <see cref="char"/>s, so an accented letter or an emoji is never split in half.
    /// </summary>
    public static string For(string? firstName, string? lastName) =>
        FirstElement(firstName) + FirstElement(lastName);

    /// <summary>
    /// Builds the monogram from a single formatted display name, e.g. "AL" for "Ada Lovelace" or "G" for "Grace".
    /// </summary>
    public static string ForDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => FirstElement(parts[0]),
            _ => FirstElement(parts[0]) + FirstElement(parts[^1]),
        };
    }

    private static string FirstElement(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        return StringInfo.GetNextTextElement(trimmed).ToUpperInvariant();
    }
}
