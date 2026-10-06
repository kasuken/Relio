using Net.Codecrete.QrCodeGenerator;
using Relio.Web.Identity;

namespace Relio.Web.Tests.Identity;

public class QrCodeSvgTests
{
    private const string Uri = "otpauth://totp/Relio:marta%40example.com?secret=ABCDEFGHIJKLMNOP&issuer=Relio&digits=6";

    [Fact]
    public void ViewBox_includes_a_four_module_quiet_zone_and_matches_the_library()
    {
        var expected = QrCode.EncodeText(Uri, QrCode.Ecc.Medium);

        var svg = QrCodeSvg.Create(Uri);

        QrCodeSvg.QuietZoneModules.Should().Be(4);
        svg.ViewBoxSize.Should().Be(expected.Size + 8);
        svg.PathData.Should().Be(expected.ToGraphicsPath(4)).And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Is_deterministic()
    {
        QrCodeSvg.Create(Uri).Should().Be(QrCodeSvg.Create(Uri));
    }

    [Fact]
    public void Different_inputs_give_different_paths()
    {
        QrCodeSvg.Create(Uri).PathData.Should().NotBe(QrCodeSvg.Create(Uri.Replace("ABCDEFGHIJKLMNOP", "PONMLKJIHGFEDCBA")).PathData);
    }

    [Fact]
    public void The_path_contains_only_path_commands_and_numbers()
    {
        // The component renders this as an attribute value; it must never carry markup.
        QrCodeSvg.Create(Uri).PathData.Should().MatchRegex(@"^[MmHhVvLlZz0-9 .,\-]+$");
    }

    [Fact]
    public void Create_rejects_empty_text()
    {
        var act = () => QrCodeSvg.Create(string.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
