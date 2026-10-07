using System.Text;
using Relio.Application.People.Import;

namespace Relio.Application.Tests.People.Import;

public sealed class TextDecoderTests
{
    [Fact]
    public void Detects_utf8_with_and_without_bom()
    {
        var plain = TextDecoder.Decode(Encoding.UTF8.GetBytes("Zoë"));
        plain.Text.Should().Be("Zoë");
        plain.Encoding.Should().Be(ImportTextEncoding.Utf8);

        var withBom = TextDecoder.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Zoë")]);
        withBom.Text.Should().Be("Zoë");
        withBom.Encoding.Should().Be(ImportTextEncoding.Utf8);
    }

    [Fact]
    public void Detects_utf16_little_and_big_endian_with_bom()
    {
        var little = TextDecoder.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Zoë")]);
        little.Text.Should().Be("Zoë");
        little.Encoding.Should().Be(ImportTextEncoding.Utf16LittleEndian);

        var big = TextDecoder.Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("Zoë")]);
        big.Text.Should().Be("Zoë");
        big.Encoding.Should().Be(ImportTextEncoding.Utf16BigEndian);
    }

    [Fact]
    public void Detects_utf16_without_bom_by_zero_bytes()
    {
        TextDecoder.Decode(Encoding.Unicode.GetBytes("Name,Email\r\nAda,ada@example.com")).Should()
            .Be(new DecodedText("Name,Email\r\nAda,ada@example.com", ImportTextEncoding.Utf16LittleEndian));
        TextDecoder.Decode(Encoding.BigEndianUnicode.GetBytes("Name,Email\r\nAda,ada@example.com")).Encoding
            .Should().Be(ImportTextEncoding.Utf16BigEndian);
    }

    [Fact]
    public void Falls_back_to_windows_1252_for_invalid_utf8()
    {
        var decoded = TextDecoder.Decode([(byte)'R', (byte)'e', (byte)'n', 0xE9, (byte)' ', 0xFC, 0xDF]);

        decoded.Text.Should().Be("René üß");
        decoded.Encoding.Should().Be(ImportTextEncoding.Windows1252);
    }

    [Fact]
    public void Strips_the_bom_character()
    {
        TextDecoder.Decode([0xEF, 0xBB, 0xBF, (byte)'a']).Text.Should().Be("a");
        TextDecoder.Decode([0xFF, 0xFE, (byte)'a', 0]).Text.Should().Be("a");
    }

    [Fact]
    public void Never_throws_on_random_bytes()
    {
        var random = new Random(29);
        for (var round = 0; round < 200; round++)
        {
            var bytes = new byte[random.Next(0, 300)];
            random.NextBytes(bytes);
            var act = () => TextDecoder.Decode(bytes);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void Charset_names_resolve_without_a_global_provider()
    {
        ImportEncodings.Get("UTF-8").Should().Be(Encoding.UTF8);
        ImportEncodings.Get("us-ascii").Should().Be(Encoding.UTF8);
        ImportEncodings.Get("ISO-8859-1").Should().Be(Encoding.Latin1);
        ImportEncodings.Get("windows-1252")!.GetString([0x80]).Should().Be("€");
        ImportEncodings.Get("ISO-8859-15")!.GetString([0xA4]).Should().Be("€");
        ImportEncodings.Get("no-such-charset").Should().BeNull();
        ImportEncodings.Get(null).Should().BeNull();
    }
}

public sealed class ImportFileDetectorTests
{
    [Theory]
    [InlineData("contacts.vcf", ImportFileKind.VCard)]
    [InlineData("CONTACTS.VCF", ImportFileKind.VCard)]
    [InlineData("a.vcard", ImportFileKind.VCard)]
    [InlineData("a.csv", ImportFileKind.Csv)]
    [InlineData("a.TSV", ImportFileKind.Csv)]
    [InlineData("a.txt", ImportFileKind.Csv)]
    public void Detects_by_extension_ignoring_case(string name, ImportFileKind expected)
    {
        ImportFileDetector.TryDetect(name, [], out var kind).Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Fact]
    public void Sniffs_a_vcard_without_a_known_extension()
    {
        ImportFixtures.Utf8("\r\n  begin:VCARD\r\nVERSION:3.0").Should().NotBeEmpty();
        ImportFileDetector.TryDetect("contacts", ImportFixtures.Utf8("\r\n  begin:VCARD\r\nVERSION:3.0"), out var kind).Should().BeTrue();
        kind.Should().Be(ImportFileKind.VCard);

        ImportFileDetector.TryDetect(null, [0xEF, 0xBB, 0xBF, .. ImportFixtures.Utf8("BEGIN:VCARD")], out kind).Should().BeTrue();
        kind.Should().Be(ImportFileKind.VCard);
    }

    [Fact]
    public void Rejects_an_image_or_unknown_text()
    {
        ImportFileDetector.TryDetect("photo.png", [0x89, 0x50, 0x4E, 0x47], out _).Should().BeFalse();
        ImportFileDetector.TryDetect("notes", ImportFixtures.Utf8("hello"), out _).Should().BeFalse();
        ImportFileDetector.TryDetect("notes", [], out _).Should().BeFalse();
    }
}
