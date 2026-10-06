using System.Globalization;
using System.Security.Cryptography;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// Computes the codes an authenticator app would show (RFC 6238, HMAC-SHA1, 30-second steps, six
/// digits), because ASP.NET Core Identity's authenticator provider can only <i>verify</i> codes -
/// <c>GenerateTwoFactorTokenAsync</c> returns an empty string for it. Test code only. Also compiled
/// into <c>Relio.Web.Tests</c> (as a linked file) for the sign-in manager's tests.
/// </summary>
public static class Totp
{
    private const int StepSeconds = 30;
    private const int Digits = 6;

    /// <summary>
    /// How many 30-second steps either side of "now" Identity's authenticator provider still accepts
    /// (<c>AuthenticatorTokenProvider</c> checks steps -2..+2).
    /// </summary>
    public const int AcceptedStepsEitherSide = 2;

    /// <summary>The code for the 30-second step that contains <paramref name="at"/>.</summary>
    /// <param name="base32Key">The shared key as shown by the setup page; spaces and any casing are ignored.</param>
    /// <param name="at">The moment to compute the code for.</param>
    public static string Compute(string base32Key, DateTimeOffset at) =>
        ComputeForStep(DecodeBase32(base32Key), at.ToUnixTimeSeconds() / StepSeconds);

    /// <summary>
    /// A six-digit code that is <i>not</i> accepted at <paramref name="at"/>: it differs from the
    /// codes of every step Identity would still accept.
    /// </summary>
    public static string CreateWrongCode(string base32Key, DateTimeOffset at)
    {
        var key = DecodeBase32(base32Key);
        var step = at.ToUnixTimeSeconds() / StepSeconds;
        var accepted = Enumerable.Range(-AcceptedStepsEitherSide, 2 * AcceptedStepsEitherSide + 1)
            .Select(offset => ComputeForStep(key, step + offset))
            .ToHashSet(StringComparer.Ordinal);

        for (var candidate = 0; candidate < 1_000_000; candidate++)
        {
            var code = candidate.ToString("D6", CultureInfo.InvariantCulture);
            if (!accepted.Contains(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Every six-digit code was accepted, which cannot happen.");
    }

    private static string ComputeForStep(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);

        var hash = HMACSHA1.HashData(key, counter);

        // RFC 4226 dynamic truncation.
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        var code = binary % (int)Math.Pow(10, Digits);
        return code.ToString("D" + Digits, CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var cleaned = input.Replace(" ", string.Empty).Replace("=", string.Empty).ToUpperInvariant();
        var bytes = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in cleaned)
        {
            var value = alphabet.IndexOf(character);
            if (value < 0)
            {
                throw new FormatException($"'{character}' is not a base32 character.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return [.. bytes];
    }
}
