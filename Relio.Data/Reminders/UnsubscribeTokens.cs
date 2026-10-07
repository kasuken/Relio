using System.Buffers.Text;
using System.Security.Cryptography;

namespace Relio.Data.Reminders;

/// <summary>
/// Generates secure tokens for one-click unsubscribe links (Issue #40).
/// A token is 32 random bytes (256 bits) from the OS CSPRNG, base64url encoded (43 chars),
/// which safely fits the column maximum length of 64 characters.
/// </summary>
public static class UnsubscribeTokens
{
    private const int TokenByteLength = 32;

    /// <summary>
    /// Generates a new cryptographically secure random token suitable for inclusion in an unsubscribe URL.
    /// </summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteLength));
}
