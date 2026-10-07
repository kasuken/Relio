namespace Relio.Application.People.Import;

/// <summary>Decides whether a chosen file is a vCard or a CSV file, from its extension and, failing that, its first bytes.</summary>
public static class ImportFileDetector
{
    /// <summary>
    /// Detects the kind. The extension decides first (<c>.vcf</c>, <c>.vcard</c> are vCard; <c>.csv</c>,
    /// <c>.tsv</c>, <c>.txt</c> are CSV); otherwise a file whose text starts with <c>BEGIN:VCARD</c> (after
    /// a byte order mark and white space, any case) is a vCard. Anything else is not a contacts file.
    /// </summary>
    public static bool TryDetect(string? fileName, ReadOnlySpan<byte> bytes, out ImportFileKind kind)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (extension.Equals(".vcf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vcard", StringComparison.OrdinalIgnoreCase))
        {
            kind = ImportFileKind.VCard;
            return true;
        }

        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            kind = ImportFileKind.Csv;
            return true;
        }

        if (StartsWithVCard(bytes))
        {
            kind = ImportFileKind.VCard;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool StartsWithVCard(ReadOnlySpan<byte> bytes)
    {
        // Sniff the first few bytes only. UTF-16 is not sniffed: such a file is named .vcf in practice.
        var head = bytes[..Math.Min(bytes.Length, 64)];
        if (head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            head = head[3..];
        }

        var start = 0;
        while (start < head.Length && head[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }

        ReadOnlySpan<byte> marker = "BEGIN:VCARD"u8;
        var rest = head[start..];
        if (rest.Length < marker.Length)
        {
            return false;
        }

        for (var index = 0; index < marker.Length; index++)
        {
            if (ToUpperAscii(rest[index]) != marker[index])
            {
                return false;
            }
        }

        return true;
    }

    private static byte ToUpperAscii(byte value) => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;
}
