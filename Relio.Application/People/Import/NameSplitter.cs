namespace Relio.Application.People.Import;

/// <summary>Splits a single full name ("Mary Jane Watson") into a first and a last name.</summary>
internal static class NameSplitter
{
    /// <summary>
    /// Splits at the last white space: "Mary Jane Watson" is "Mary Jane" and "Watson". One word is a first
    /// name only. Blank gives nothing.
    /// </summary>
    public static (string? First, string? Last) Split(string? fullName)
    {
        var words = (fullName ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => (null, null),
            1 => (words[0], null),
            _ => (string.Join(' ', words[..^1]), words[^1]),
        };
    }
}
