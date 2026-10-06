namespace Relio.Web.Identity;

/// <summary>
/// Tidies what a person typed into a two-factor code field before it reaches Identity (issue #20).
/// Authenticator apps show codes as <c>123 456</c> and recovery codes are copied from a password
/// manager with stray spaces or in lower case; Identity itself is strict (<c>int.TryParse</c> fails on
/// a space, recovery codes are compared exactly), so the pages normalise first. Pure functions.
/// </summary>
public static class TwoFactorCodes
{
    /// <summary>
    /// Removes spaces and hyphens from an authenticator code and checks that exactly
    /// <see cref="AuthenticatorUri.CodeDigits"/> digits remain. A malformed value is reported as
    /// <see langword="false"/> so the page can answer "enter the 6-digit code" without spending one
    /// of the account's failed-attempt budget on a typo.
    /// </summary>
    public static bool TryNormalizeAuthenticatorCode(string? input, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = input.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (digits.Length != AuthenticatorUri.CodeDigits || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        code = digits;
        return true;
    }

    /// <summary>
    /// Trims, removes spaces and upper-cases a recovery code (Identity's are <c>XXXXX-XXXXX</c> in
    /// upper case and compared exactly). Hyphens are kept: they are part of the code.
    /// </summary>
    public static string NormalizeRecoveryCode(string? input) =>
        (input ?? string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
