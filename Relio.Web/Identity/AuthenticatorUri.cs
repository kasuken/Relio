using System.Globalization;
using System.Text;

namespace Relio.Web.Identity;

/// <summary>
/// Builds the <c>otpauth://</c> URI an authenticator app reads from a QR code (issue #20), and the
/// grouped form of the key people type when they can not scan. Pure functions: nothing here is
/// logged, stored or sent anywhere - the URI contains the shared secret, so it is a credential.
/// </summary>
/// <remarks>
/// The format is the de-facto "Key URI Format" every authenticator app implements:
/// <c>otpauth://totp/{issuer}:{account}?secret={base32}&amp;issuer={issuer}&amp;digits=6</c>. The
/// period (30 seconds) and algorithm (SHA-1) are left at their defaults because those are what
/// ASP.NET Core Identity's authenticator provider verifies.
/// </remarks>
public static class AuthenticatorUri
{
    /// <summary>The name an authenticator app shows next to the account.</summary>
    public const string Issuer = "Relio";

    /// <summary>The number of digits in a code; also what Identity's authenticator provider expects.</summary>
    public const int CodeDigits = 6;

    /// <summary>Builds the provisioning URI for <paramref name="accountLabel"/> (the user's email) and the unformatted base32 key.</summary>
    /// <exception cref="ArgumentException">Either argument is blank.</exception>
    public static string Build(string accountLabel, string unformattedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(unformattedKey);

        var issuer = Uri.EscapeDataString(Issuer);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"otpauth://totp/{issuer}:{Uri.EscapeDataString(accountLabel)}?secret={unformattedKey}&issuer={issuer}&digits={CodeDigits}");
    }

    /// <summary>
    /// Lowercase groups of four separated by spaces (<c>abcd efgh ij</c>), the way authenticator
    /// apps display a key and the easiest shape to read aloud and type. The page accepts it back
    /// with the spaces and any casing.
    /// </summary>
    public static string FormatKey(string unformattedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unformattedKey);

        var builder = new StringBuilder(unformattedKey.Length + unformattedKey.Length / 4);
        for (var i = 0; i < unformattedKey.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                builder.Append(' ');
            }

            builder.Append(char.ToLowerInvariant(unformattedKey[i]));
        }

        return builder.ToString();
    }
}
