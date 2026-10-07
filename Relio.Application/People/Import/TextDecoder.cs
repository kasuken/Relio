using System.Text;

namespace Relio.Application.People.Import;

/// <summary>The encoding <see cref="TextDecoder"/> settled on.</summary>
public enum ImportTextEncoding
{
    /// <summary>UTF-8, with or without a byte order mark.</summary>
    Utf8,

    /// <summary>UTF-16, little endian.</summary>
    Utf16LittleEndian,

    /// <summary>UTF-16, big endian.</summary>
    Utf16BigEndian,

    /// <summary>Windows-1252: the fallback for bytes that are not valid UTF-8 (older Outlook and Windows exports).</summary>
    Windows1252,
}

/// <summary>Decoded file text and the encoding it was decoded with. Personal data: never log it.</summary>
public sealed record DecodedText(string Text, ImportTextEncoding Encoding);

/// <summary>
/// Turns a file's bytes into text without being told the encoding: a byte order mark decides first,
/// then UTF-16 without one (most bytes at odd or even positions are zero), then strict UTF-8, and
/// finally Windows-1252 for anything that is not valid UTF-8. Never throws on content.
/// </summary>
public static class TextDecoder
{
    private const int SniffLength = 1_024;
    private const double ZeroShare = 0.30;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Decodes <paramref name="bytes"/>; the byte order mark, if any, is not part of the text.</summary>
    public static DecodedText Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return new DecodedText(Encoding.UTF8.GetString(bytes[3..]), ImportTextEncoding.Utf8);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return new DecodedText(Encoding.Unicode.GetString(bytes[2..]), ImportTextEncoding.Utf16LittleEndian);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return new DecodedText(Encoding.BigEndianUnicode.GetString(bytes[2..]), ImportTextEncoding.Utf16BigEndian);
        }

        switch (SniffUtf16(bytes))
        {
            case ImportTextEncoding.Utf16LittleEndian:
                return new DecodedText(Encoding.Unicode.GetString(bytes), ImportTextEncoding.Utf16LittleEndian);
            case ImportTextEncoding.Utf16BigEndian:
                return new DecodedText(Encoding.BigEndianUnicode.GetString(bytes), ImportTextEncoding.Utf16BigEndian);
        }

        try
        {
            return new DecodedText(StrictUtf8.GetString(bytes), ImportTextEncoding.Utf8);
        }
        catch (DecoderFallbackException)
        {
            return new DecodedText(Windows1252().GetString(bytes), ImportTextEncoding.Windows1252);
        }
    }

    /// <summary>Whether the bytes are UTF-16 (a byte order mark, or mostly zero bytes at odd or even positions).</summary>
    internal static bool IsUtf16(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])
        || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])
        || SniffUtf16(bytes) is not ImportTextEncoding.Utf8;

    /// <summary>Windows-1252 through the code pages provider, which is not registered globally (library code never registers one).</summary>
    internal static Encoding Windows1252() => CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;

    private static ImportTextEncoding SniffUtf16(ReadOnlySpan<byte> bytes)
    {
        var length = Math.Min(bytes.Length, SniffLength);
        if (length < 4)
        {
            return ImportTextEncoding.Utf8;
        }

        var zerosAtOdd = 0;
        var zerosAtEven = 0;
        for (var index = 0; index < length; index++)
        {
            if (bytes[index] != 0)
            {
                continue;
            }

            if (index % 2 == 1)
            {
                zerosAtOdd++;
            }
            else
            {
                zerosAtEven++;
            }
        }

        var perParity = length / 2.0;
        if (zerosAtOdd >= perParity * ZeroShare && zerosAtOdd > zerosAtEven)
        {
            return ImportTextEncoding.Utf16LittleEndian;
        }

        if (zerosAtEven >= perParity * ZeroShare && zerosAtEven > zerosAtOdd)
        {
            return ImportTextEncoding.Utf16BigEndian;
        }

        return ImportTextEncoding.Utf8;
    }
}
