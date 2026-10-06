using Net.Codecrete.QrCodeGenerator;

namespace Relio.Web.Identity;

/// <summary>
/// A QR code reduced to what an inline SVG needs: a square view box and one path (issue #20). The
/// code is generated on the server, in-process, by <c>Net.Codecrete.QrCodeGenerator</c> - the
/// shared secret it encodes never leaves Relio, which is why there is no third-party QR service and
/// no client-side script. Colours are deliberately not part of this type: the component that renders
/// it uses CSS tokens (see <c>QrCodeImage.razor</c> and the design system's QR tokens).
/// </summary>
/// <param name="ViewBoxSize">The width and height of the SVG view box, in modules, including the quiet zone.</param>
/// <param name="PathData">The SVG path data (<c>d</c> attribute) drawing every dark module.</param>
public sealed record QrCodeSvg(int ViewBoxSize, string PathData)
{
    /// <summary>
    /// The empty border, in modules, around the code. The QR specification asks for four; scanners
    /// need it to find the code's edges, and it is why the code stays scannable on any page colour.
    /// </summary>
    public const int QuietZoneModules = 4;

    /// <summary>
    /// Encodes <paramref name="text"/> at medium error correction (about 15% of the code can be
    /// damaged or covered and it still scans - plenty for a screen, while keeping the code small
    /// enough to scan from a laptop across a desk).
    /// </summary>
    public static QrCodeSvg Create(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        var code = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        return new QrCodeSvg(code.Size + 2 * QuietZoneModules, code.ToGraphicsPath(QuietZoneModules));
    }
}
