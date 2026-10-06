using Relio.Web.Identity;

namespace Relio.Web.Tests.Identity;

public class AuthenticatorUriTests
{
    [Fact]
    public void Build_produces_the_otpauth_uri()
    {
        AuthenticatorUri.Build("marta@example.com", "ABCDEFGHIJKLMNOP")
            .Should().Be("otpauth://totp/Relio:marta%40example.com?secret=ABCDEFGHIJKLMNOP&issuer=Relio&digits=6");
    }

    [Fact]
    public void Build_escapes_reserved_characters_in_the_label()
    {
        AuthenticatorUri.Build("a+b@x.com", "ABCDEFGHIJKLMNOP")
            .Should().Contain("Relio:a%2Bb%40x.com?");
    }

    [Theory]
    [InlineData("", "ABCDEFGHIJKLMNOP")]
    [InlineData("  ", "ABCDEFGHIJKLMNOP")]
    [InlineData("marta@example.com", "")]
    [InlineData("marta@example.com", "   ")]
    public void Build_rejects_blank_input(string label, string key)
    {
        var act = () => AuthenticatorUri.Build(label, key);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("ABCDEFGHIJ", "abcd efgh ij")]
    [InlineData("ABCD", "abcd")]
    [InlineData("ABCDEFGH", "abcd efgh")]
    [InlineData("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", "jbsw y3dp ehpk 3pxp jbsw y3dp ehpk 3pxp")]
    public void FormatKey_groups_lowercase_in_fours(string key, string expected)
    {
        AuthenticatorUri.FormatKey(key).Should().Be(expected);
    }

    [Fact]
    public void FormatKey_rejects_blank_input()
    {
        var act = () => AuthenticatorUri.FormatKey(" ");

        act.Should().Throw<ArgumentException>();
    }
}
