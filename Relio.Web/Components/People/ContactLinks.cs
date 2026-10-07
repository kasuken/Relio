using System.Text.RegularExpressions;
using Relio.Application.People;
using Relio.Domain;

namespace Relio.Web.Components.People;

/// <summary>
/// The only place an <c>href</c> is built from a contact method. Blazor does not sanitise
/// attribute values, so a value bound straight into <c>href</c> would let someone store
/// <c>javascript:...</c> as a "social handle" and run it on click. Instead only two schemes are
/// ever produced, each from data that has already been validated, and every other kind (address,
/// social, other) gets no link at all.
/// </summary>
public static partial class ContactLinks
{
    /// <summary>
    /// The link for a contact method, or <see langword="null"/> when it should be plain text.
    /// Email: <c>mailto:</c> plus the address, but only when it is made of the ordinary characters
    /// of an address - nothing that could start a query (<c>?subject=</c>) or add a recipient
    /// (<c>,</c>). Phone: <c>tel:</c> plus the normalized number (a leading + and digits), when it
    /// has at least <see cref="ContactMethodRules.MinPhoneDigits"/> digits.
    /// </summary>
    /// <param name="kind">What sort of detail it is.</param>
    /// <param name="value">The stored value.</param>
    /// <param name="normalizedValue">The stored comparison key, which for a phone is the dialable form.</param>
    public static string? HrefFor(ContactMethodKind kind, string value, string normalizedValue)
    {
        switch (kind)
        {
            case ContactMethodKind.Email:
                var address = (value ?? string.Empty).Trim();
                return SafeEmail().IsMatch(address) ? "mailto:" + address : null;

            case ContactMethodKind.Phone:
                var digits = (normalizedValue ?? string.Empty).Count(character => character is >= '0' and <= '9');
                return DialableNumber().IsMatch(normalizedValue ?? string.Empty) && digits >= ContactMethodRules.MinPhoneDigits
                    ? "tel:" + normalizedValue
                    : null;

            default:
                return null;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+$")]
    private static partial Regex SafeEmail();

    [GeneratedRegex(@"^\+?[0-9]+$")]
    private static partial Regex DialableNumber();
}
