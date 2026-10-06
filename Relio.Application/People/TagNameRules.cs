using System.Globalization;
using System.Text.RegularExpressions;

namespace Relio.Application.People;

/// <summary>
/// The rules for a tag name typed into the person form (issue #24), as pure functions. A name the
/// user types is matched against their existing tags <b>in code</b> - the InMemory test provider is
/// case-sensitive, so the database cannot be relied on to do it - and the unique
/// <c>(OwnerId, Name)</c> index on SQL Server remains the authority.
/// </summary>
public static partial class TagNameRules
{
    /// <summary>The most tags one person can have.</summary>
    public const int MaxPerPerson = 20;

    /// <summary>
    /// Compares tag names the way the SQL Server column does, as closely as .NET can: case,
    /// kana type and character width are ignored; accents are not (<c>Cafe</c> and <c>Café</c> are
    /// different tags, as under <c>Latin1_General_CI_AS</c>). It approximates a database collation
    /// rather than reproducing it, which is why a race that the approximation misses still ends in
    /// a calm <c>TagNameConflict</c> from the unique index.
    /// </summary>
    public static readonly StringComparer Comparer = StringComparer.Create(
        CultureInfo.InvariantCulture,
        CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth);

    /// <summary>
    /// Trims a name and collapses every run of whitespace to one space, keeping the casing the user
    /// typed. Blank becomes <see langword="null"/> ("no tag").
    /// </summary>
    public static string? Normalize(string? name)
    {
        var collapsed = WhitespaceRun().Replace(name ?? string.Empty, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
