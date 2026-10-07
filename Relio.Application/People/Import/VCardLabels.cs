using Relio.Domain;

namespace Relio.Application.People.Import;

/// <summary>Chooses the label a contact detail gets from a vCard: the group's <c>X-ABLabel</c> first, then its <c>TYPE</c>.</summary>
internal static class VCardLabels
{
    private const string AppleLabelStart = "_$!<";
    private const string AppleLabelEnd = ">!$_";

    /// <summary>
    /// The label for a property: Apple's <c>item1.X-ABLabel</c> (<c>_$!&lt;Mobile&gt;!$_</c> becomes "Mobile", the
    /// "Other" label becomes no label), else a <c>TYPE</c> (cell, home, work, main, iPhone). PREF, VOICE,
    /// INTERNET and X400 are ignored. A label longer than <see cref="ContactMethod.LabelMaxLength"/> becomes no label.
    /// </summary>
    public static string? From(ContactMethodKind kind, IReadOnlyList<string> types, string? groupLabel)
    {
        _ = kind;

        var fromGroup = FromGroup(groupLabel);
        if (fromGroup is not null)
        {
            return fromGroup;
        }

        if (!string.IsNullOrWhiteSpace(groupLabel))
        {
            // The group's label was the "Other" placeholder: that is "no label", not a reason to use a type.
            return null;
        }

        foreach (var type in types)
        {
            var label = type switch
            {
                "iphone" => "iPhone",
                "cell" or "mobile" => "Mobile",
                "home" => "Home",
                "work" => "Work",
                "main" => "Main",
                _ => null,
            };

            if (label is not null)
            {
                return label;
            }
        }

        return null;
    }

    private static string? FromGroup(string? groupLabel)
    {
        var label = groupLabel?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            return null;
        }

        if (label.StartsWith(AppleLabelStart, StringComparison.Ordinal) && label.EndsWith(AppleLabelEnd, StringComparison.Ordinal))
        {
            label = label[AppleLabelStart.Length..^AppleLabelEnd.Length].Trim();
        }

        if (label.Length == 0 || label.Equals("Other", StringComparison.OrdinalIgnoreCase) || label.Length > ContactMethod.LabelMaxLength)
        {
            return null;
        }

        return label;
    }
}
