using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Relio.Data.Administration;

/// <summary>
/// Generates and hashes invitation tokens. A token is 32 random bytes (256 bits) from the
/// operating system's CSPRNG, base64url encoded so it is safe in a URL; only its SHA-256 hash is
/// ever stored. A plain, fast hash is the right tool here (unlike for passwords): the input already
/// has 256 bits of entropy, so there is nothing to brute-force.
/// </summary>
public static class InvitationTokens
{
    private const int TokenByteLength = 32;

    /// <summary>A new random token to put in an invitation link.</summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteLength));

    /// <summary>The hash stored for <paramref name="token"/> (upper-case hex, 64 characters).</summary>
    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
