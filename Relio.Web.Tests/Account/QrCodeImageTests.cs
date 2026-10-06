using Bunit;
using Relio.Web.Components.Account.Shared;
using Relio.Web.Identity;

namespace Relio.Web.Tests.Account;

public class QrCodeImageTests : BunitContext
{
    private static readonly QrCodeSvg Code = QrCodeSvg.Create(
        "otpauth://totp/Relio:marta%40example.com?secret=ABCDEFGHIJKLMNOP&issuer=Relio&digits=6");

    [Fact]
    public void Renders_an_accessible_inline_svg()
    {
        var cut = Render<QrCodeImage>(parameters => parameters
            .Add(p => p.Code, Code)
            .Add(p => p.Label, "QR code for adding Relio to your authenticator app")
            .AddUnmatched("data-testid", "manage-2fa-qr"));

        var svg = cut.Find("svg");
        svg.GetAttribute("role").Should().Be("img");
        svg.GetAttribute("aria-label").Should().Be("QR code for adding Relio to your authenticator app");
        svg.GetAttribute("viewBox").Should().Be($"0 0 {Code.ViewBoxSize} {Code.ViewBoxSize}");
        svg.GetAttribute("data-testid").Should().Be("manage-2fa-qr");
        svg.ClassList.Should().Contain("rl-qr");
        cut.Find("rect.rl-qr-ground").Should().NotBeNull();
        cut.Find("path.rl-qr-ink").GetAttribute("d").Should().Be(Code.PathData).And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Loads_nothing_and_runs_nothing()
    {
        var cut = Render<QrCodeImage>(parameters => parameters
            .Add(p => p.Code, Code)
            .Add(p => p.Label, "QR code"));

        cut.FindAll("script, image, img, use, a, foreignObject").Should().BeEmpty();
        cut.Markup.Should().NotContain("href").And.NotContain("xlink").And.NotContain("http");
    }

    [Fact]
    public void Draws_with_classes_so_the_colours_come_from_the_design_tokens()
    {
        var cut = Render<QrCodeImage>(parameters => parameters
            .Add(p => p.Code, Code)
            .Add(p => p.Label, "QR code"));

        cut.Markup.Should().NotContain("fill=").And.NotContain("stroke=").And.NotContain("style=");
    }
}
