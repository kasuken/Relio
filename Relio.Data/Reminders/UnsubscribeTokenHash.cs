using System.Security.Cryptography;
using System.Text;

namespace Relio.Data.Reminders;

/// <summary>
/// Computes the indexed, one-way verifier for high-entropy unsubscribe tokens.
/// </summary>
public static class UnsubscribeTokenHash
{
    /// <summary>Returns the uppercase SHA-256 hex digest for a token, preserving <see langword="null"/>.</summary>
    public static string? Compute(string? token) =>
        token is null
            ? null
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
