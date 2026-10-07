using Relio.Domain;

namespace Relio.Web.Components.People;

/// <summary>
/// The labels offered while typing a contact method's optional label. Only suggestions: the field
/// accepts any text, so "Mum's work" is as welcome as "Work".
/// </summary>
public static class ContactLabelSuggestions
{
    /// <summary>The suggestions for <paramref name="kind"/> that start with <paramref name="text"/> (ignoring case); all of them for blank text.</summary>
    public static IReadOnlyList<string> For(ContactMethodKind kind, string? text)
    {
        var candidates = kind switch
        {
            ContactMethodKind.Email => new[] { "Personal", "Work" },
            ContactMethodKind.Phone => new[] { "Mobile", "Home", "Work" },
            ContactMethodKind.Address => new[] { "Home", "Work" },
            ContactMethodKind.Social => new[] { "Instagram", "LinkedIn", "Mastodon", "Bluesky", "Facebook" },
            _ => Array.Empty<string>(),
        };

        var typed = text?.Trim();
        return string.IsNullOrEmpty(typed)
            ? candidates
            : [.. candidates.Where(candidate => candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
    }
}
