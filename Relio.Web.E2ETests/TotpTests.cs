using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

/// <summary>
/// Checks the test-only TOTP calculator itself against the published RFC 6238 vector, so a bug in
/// it can never be mistaken for a bug in the app.
/// </summary>
public class TotpTests
{
    // RFC 6238 appendix B: the SHA-1 secret is the ASCII string "12345678901234567890".
    private const string Rfc6238Key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Fact]
    public void Matches_the_RFC_6238_SHA1_vector()
    {
        // T = 59 s gives the 8-digit code 94287082 in the RFC; six digits are its last six.
        Totp.Compute(Rfc6238Key, DateTimeOffset.FromUnixTimeSeconds(59)).Should().Be("287082");
    }

    [Fact]
    public void Matches_a_second_RFC_6238_SHA1_vector()
    {
        // T = 1111111109 s gives 07081804 in the RFC.
        Totp.Compute(Rfc6238Key, DateTimeOffset.FromUnixTimeSeconds(1111111109)).Should().Be("081804");
    }

    [Fact]
    public void Ignores_spaces_and_case_in_the_key()
    {
        Totp.Compute("gezd gnbv gy3t qojq gezd gnbv gy3t qojq", DateTimeOffset.FromUnixTimeSeconds(59)).Should().Be("287082");
    }

    [Fact]
    public void Wrong_code_is_outside_the_accepted_window()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var wrong = Totp.CreateWrongCode(Rfc6238Key, now);

        wrong.Should().HaveLength(6);
        for (var step = -Totp.AcceptedStepsEitherSide; step <= Totp.AcceptedStepsEitherSide; step++)
        {
            Totp.Compute(Rfc6238Key, now.AddSeconds(30 * step)).Should().NotBe(wrong);
        }
    }
}
