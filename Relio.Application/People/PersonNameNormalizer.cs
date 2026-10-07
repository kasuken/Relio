using System.Globalization;
using System.Text;

namespace Relio.Application.People;

/// <summary>
/// Reduces a name to the key two names are compared by: no case, no accents, no apostrophes, no
/// punctuation, single spaces. Pure and culture-independent. Issue #27 (duplicate detection) uses it
/// through <see cref="PossibleDuplicateMatcher"/>; issue #29 (import) reuses the matcher, and issue
/// #52 (search) must normalize both the query and the names the same way, with this class.
/// </summary>
/// <remarks>
/// <para>
/// The steps, in this order (each is pinned by a test):
/// apostrophes are removed first (U+00B4 would otherwise decompose into a space);
/// compatibility decomposition (<c>FormKD</c>: full-width letters and ligatures fold to plain letters,
/// accents separate from their letter); non-spacing marks are dropped; the text is lower-cased with
/// the invariant culture - <b>after</b> the marks are gone, because U+0130 (capital I with dot) only
/// becomes <c>i</c> that way; letters with no decomposition are mapped by hand (ß, æ, œ, ø, ł, đ, ð, þ, dotless i);
/// every other punctuation mark or symbol becomes a space (<c>Mary-Jane</c> is <c>mary jane</c>);
/// whitespace runs collapse to one space and the ends are trimmed; the result is recomposed
/// (<c>FormC</c>), which restores non-Latin scripts such as Hangul.
/// </para>
/// <para>
/// Dropping non-spacing marks is lossy for some scripts (Japanese voiced-sound marks, some Indic
/// vowel signs). That is acceptable: the normalizer is only used to <i>find</i> possible matches, so
/// losing a distinction can only add a warning, never hide one.
/// </para>
/// <para>
/// <b>Personal data.</b> A normalized name is a copy of what the user typed. It is computed in memory
/// for the comparison and thrown away: never persist it, cache it, log it or put it in a URL.
/// </para>
/// </remarks>
public static class PersonNameNormalizer
{
    // Apostrophe, right single quote, modifier letter apostrophe, grave accent, acute accent.
    private static readonly char[] Apostrophes = ['\'', '’', 'ʼ', '`', '´'];

    /// <summary>Normalizes <paramref name="value"/>; <see langword="null"/> or blank gives the empty string. Idempotent.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = RemoveApostrophes(value).Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var original in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(original) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var character = char.ToLowerInvariant(original);

            if (char.IsWhiteSpace(character) || char.IsControl(character) || char.IsPunctuation(character) || char.IsSymbol(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            switch (character)
            {
                case 'ß':
                    builder.Append("ss");
                    break;
                case 'æ':
                    builder.Append("ae");
                    break;
                case 'œ':
                    builder.Append("oe");
                    break;
                case 'ø':
                    builder.Append('o');
                    break;
                case 'ł':
                    builder.Append('l');
                    break;
                case 'đ' or 'ð':
                    builder.Append('d');
                    break;
                case 'þ':
                    builder.Append("th");
                    break;
                case 'ı':
                    builder.Append('i');
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The normalized full name: <paramref name="firstName"/> and <paramref name="lastName"/> joined by
    /// one space and normalized together, so a whole name typed into the first name field equals the
    /// same name split over both.
    /// </summary>
    public static string FullName(string? firstName, string? lastName) =>
        Normalize((firstName ?? string.Empty) + " " + (lastName ?? string.Empty));

    private static string RemoveApostrophes(string value)
    {
        if (value.AsSpan().IndexOfAny(Apostrophes) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (Array.IndexOf(Apostrophes, character) < 0)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
