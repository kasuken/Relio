using Relio.Web.Identity;

namespace Relio.Web.Tests.Identity;

public class TwoFactorCodesTests
{
    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("123 456", "123456")]
    [InlineData(" 123-456 ", "123456")]
    [InlineData("012345", "012345")]
    public void TryNormalizeAuthenticatorCode_accepts_six_digits_with_spaces_or_hyphens(string input, string expected)
    {
        TwoFactorCodes.TryNormalizeAuthenticatorCode(input, out var code).Should().BeTrue();

        code.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("abcdef")]
    [InlineData("12 34 5")]
    [InlineData("١٢٣٤٥٦")]
    public void TryNormalizeAuthenticatorCode_rejects_anything_else(string? input)
    {
        TwoFactorCodes.TryNormalizeAuthenticatorCode(input, out var code).Should().BeFalse();

        code.Should().BeEmpty();
    }

    [Theory]
    [InlineData("abcde-fghjk", "ABCDE-FGHJK")]
    [InlineData("  ABCDE-FGHJK  ", "ABCDE-FGHJK")]
    [InlineData("abcde - fghjk", "ABCDE-FGHJK")]
    [InlineData(null, "")]
    public void NormalizeRecoveryCode_uppercases_and_removes_spaces_but_keeps_the_hyphen(string? input, string expected)
    {
        TwoFactorCodes.NormalizeRecoveryCode(input).Should().Be(expected);
    }
}
