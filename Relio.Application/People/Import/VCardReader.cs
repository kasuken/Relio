using System.Text;
using Relio.Domain;

namespace Relio.Application.People.Import;

/// <summary>
/// Reads people from a vCard file (versions 2.1, 3.0 and 4.0): a read-only subset, hand-rolled and
/// pure, with no package and no I/O (issue #29). It reads the name (<c>N</c>, else <c>FN</c>), the
/// nickname, the birthday (<c>BDAY</c>, including Apple's year-less forms), the note, and email,
/// phone, address and social profile contact methods. Everything else (organisation, photo,
/// categories, URLs, instant messaging, <c>X-</c> extensions) is ignored.
/// </summary>
/// <remarks>
/// <para>
/// <b>Folding and encodings.</b> Unfolding happens on the <i>bytes</i> before decoding, so a multi-byte
/// UTF-8 character that an exporter split across a fold survives (UTF-16 files are decoded first, since
/// their newlines are not single bytes). Quoted-printable values (vCard 2.1, Android) are decoded to
/// bytes and then to text in the property's <c>CHARSET</c> (UTF-8 by default, falling back to Windows-1252
/// for bytes that are not valid UTF-8), and a trailing <c>=</c> joins the next line. Structured values are
/// split on unescaped separators <i>before</i> decoding, so an encoded <c>;</c> stays part of its value.
/// </para>
/// <para>
/// <b>Bounds.</b> A line longer than <see cref="ImportLimits.MaxVCardLineLength"/> is skipped, and
/// <c>PHOTO</c>, <c>LOGO</c>, <c>SOUND</c> and <c>KEY</c> lines are skipped without being decoded. At most
/// <see cref="ImportLimits.MaxPeople"/> people are kept. Malformed lines are ignored, never thrown.
/// </para>
/// <para>
/// <b>Personal data.</b> Nothing here stores or logs the file or any value. The reader keeps no state.
/// </para>
/// </remarks>
public static class VCardReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads every card in <paramref name="bytes"/>.</summary>
    public static ImportReadResult Read(ReadOnlySpan<byte> bytes)
    {
        var text = TextDecoder.IsUtf16(bytes)
            ? UnfoldText(TextDecoder.Decode(bytes).Text)
            : TextDecoder.Decode(UnfoldBytes(bytes)).Text;

        var people = new List<ImportPersonDraft>();
        var skippedEmpty = 0;
        var cardNumber = 0;
        List<Property>? card = null;
        var depth = 0;

        var position = 0;
        while (TryNextLine(text, ref position, out var line))
        {
            if (line.IsEmpty || line.Length > ImportLimits.MaxVCardLineLength)
            {
                continue;
            }

            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase))
            {
                if (card is null)
                {
                    if (line[6..].Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase))
                    {
                        card = [];
                        depth = 0;
                        cardNumber++;
                    }
                }
                else
                {
                    depth++;
                }

                continue;
            }

            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase))
            {
                if (card is null)
                {
                    continue;
                }

                if (depth > 0)
                {
                    depth--;
                    continue;
                }

                if (line[4..].Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    Finish(cardNumber, card, people, ref skippedEmpty);
                    card = null;
                }

                continue;
            }

            if (card is null || depth > 0)
            {
                continue;
            }

            var property = ReadProperty(text, ref position, line);
            if (property is not null)
            {
                card.Add(property);
            }
        }

        if (card is { Count: > 0 })
        {
            // A card cut off at the end of the file is kept when it has any content.
            Finish(cardNumber, card, people, ref skippedEmpty);
        }

        return ImportReadResult.Create(people, skippedEmpty);
    }

    private static void Finish(int cardNumber, List<Property> properties, List<ImportPersonDraft> people, ref int skippedEmpty)
    {
        var draft = ToDraft(cardNumber, properties);
        if (draft.IsEmpty)
        {
            skippedEmpty++;
        }
        else
        {
            people.Add(draft);
        }
    }

    // ---- Unfolding -------------------------------------------------------------------------------

    /// <summary>Removes each line break that is followed by one space or tab, on bytes, so a split multi-byte character is rejoined.</summary>
    private static byte[] UnfoldBytes(ReadOnlySpan<byte> bytes)
    {
        var result = new byte[bytes.Length];
        var count = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            if (value is not ((byte)'\r' or (byte)'\n'))
            {
                result[count++] = value;
                continue;
            }

            var last = index;
            if (value == (byte)'\r' && last + 1 < bytes.Length && bytes[last + 1] == (byte)'\n')
            {
                last++;
            }

            if (last + 1 < bytes.Length && bytes[last + 1] is (byte)' ' or (byte)'\t')
            {
                index = last + 1;
                continue;
            }

            for (; index <= last; index++)
            {
                result[count++] = bytes[index];
            }

            index = last;
        }

        return result.AsSpan(0, count).ToArray();
    }

    private static string UnfoldText(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (value is not ('\r' or '\n'))
            {
                builder.Append(value);
                continue;
            }

            var last = index;
            if (value == '\r' && last + 1 < text.Length && text[last + 1] == '\n')
            {
                last++;
            }

            if (last + 1 < text.Length && text[last + 1] is ' ' or '\t')
            {
                index = last + 1;
                continue;
            }

            builder.Append(text, index, last - index + 1);
            index = last;
        }

        return builder.ToString();
    }

    private static bool TryNextLine(string text, scoped ref int position, out ReadOnlySpan<char> line)
    {
        if (position >= text.Length)
        {
            line = default;
            return false;
        }

        var span = text.AsSpan(position);
        var end = span.IndexOfAny('\r', '\n');
        if (end < 0)
        {
            line = span;
            position = text.Length;
            return true;
        }

        line = span[..end];
        var next = position + end + 1;
        if (span[end] == '\r' && next < text.Length && text[next] == '\n')
        {
            next++;
        }

        position = next;
        return true;
    }

    // ---- Properties ------------------------------------------------------------------------------

    private sealed class Property
    {
        public string? Group { get; init; }

        public required string Name { get; init; }

        public required string Raw { get; init; }

        public bool QuotedPrintable { get; init; }

        public bool Base64 { get; init; }

        public Encoding? Charset { get; init; }

        public List<string> Types { get; } = [];

        public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Property? ReadProperty(string text, ref int position, ReadOnlySpan<char> line)
    {
        var colon = IndexOutsideQuotes(line, ':');
        if (colon <= 0)
        {
            return null;
        }

        var header = line[..colon];
        var name = PropertyName(header);
        if (IsSkipped(name))
        {
            return null;
        }

        string content;
        var isQuotedPrintable = header.Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
        if (isQuotedPrintable && line.EndsWith("="))
        {
            // vCard 2.1 soft line breaks: a trailing "=" joins the next physical line, nothing removed.
            var builder = new StringBuilder();
            var current = line;
            while (true)
            {
                var soft = current.EndsWith("=");
                builder.Append(soft ? current[..^1] : current);
                if (!soft || builder.Length > ImportLimits.MaxVCardLineLength || !TryNextLine(text, ref position, out current))
                {
                    break;
                }
            }

            if (builder.Length > ImportLimits.MaxVCardLineLength)
            {
                return null;
            }

            content = builder.ToString();
        }
        else
        {
            content = line.ToString();
        }

        if (!IsWanted(name))
        {
            return null;
        }

        return Parse(content);
    }

    private static string PropertyName(ReadOnlySpan<char> header)
    {
        var semicolon = header.IndexOf(';');
        var name = (semicolon < 0 ? header : header[..semicolon]).Trim();
        var dot = name.IndexOf('.');
        return (dot < 0 ? name : name[(dot + 1)..]).ToString();
    }

    private static bool IsSkipped(string name) =>
        name.Equals("PHOTO", StringComparison.OrdinalIgnoreCase)
        || name.Equals("LOGO", StringComparison.OrdinalIgnoreCase)
        || name.Equals("SOUND", StringComparison.OrdinalIgnoreCase)
        || name.Equals("KEY", StringComparison.OrdinalIgnoreCase);

    private static bool IsWanted(string name) =>
        name.Equals("VERSION", StringComparison.OrdinalIgnoreCase)
        || name.Equals("N", StringComparison.OrdinalIgnoreCase)
        || name.Equals("FN", StringComparison.OrdinalIgnoreCase)
        || name.Equals("NICKNAME", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BDAY", StringComparison.OrdinalIgnoreCase)
        || name.Equals("NOTE", StringComparison.OrdinalIgnoreCase)
        || name.Equals("EMAIL", StringComparison.OrdinalIgnoreCase)
        || name.Equals("TEL", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ADR", StringComparison.OrdinalIgnoreCase)
        || name.Equals("X-SOCIALPROFILE", StringComparison.OrdinalIgnoreCase)
        || name.Equals("X-ABLABEL", StringComparison.OrdinalIgnoreCase);

    private static Property? Parse(string line)
    {
        var colon = IndexOutsideQuotes(line, ':');
        if (colon <= 0)
        {
            return null;
        }

        var segments = SplitOutsideQuotes(line[..colon], ';');
        var nameSegment = segments[0].Trim();
        string? group = null;
        var dot = nameSegment.IndexOf('.');
        if (dot >= 0)
        {
            group = nameSegment[..dot];
            nameSegment = nameSegment[(dot + 1)..];
        }

        var quotedPrintable = false;
        var base64 = false;
        Encoding? charset = null;
        var types = new List<string>();
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in segments.Skip(1))
        {
            var text = segment.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var equals = text.IndexOf('=');
            if (equals < 0)
            {
                // vCard 2.1 bare parameter: an encoding, or a TYPE value (TEL;CELL;VOICE).
                if (text.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
                {
                    quotedPrintable = true;
                }
                else if (text.Equals("BASE64", StringComparison.OrdinalIgnoreCase) || text.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    base64 = true;
                }
                else
                {
                    types.Add(text.ToLowerInvariant());
                }

                continue;
            }

            var key = text[..equals].Trim();
            var value = Unquote(text[(equals + 1)..].Trim());
            if (key.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
            {
                types.AddRange(value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(type => type.ToLowerInvariant()));
            }
            else if (key.Equals("ENCODING", StringComparison.OrdinalIgnoreCase))
            {
                quotedPrintable |= value.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
                base64 |= value.Equals("B", StringComparison.OrdinalIgnoreCase) || value.Equals("BASE64", StringComparison.OrdinalIgnoreCase);
            }
            else if (key.Equals("CHARSET", StringComparison.OrdinalIgnoreCase))
            {
                charset = ImportEncodings.Get(value);
            }
            else
            {
                parameters.TryAdd(key, value);
            }
        }

        var property = new Property
        {
            Group = group,
            Name = nameSegment,
            Raw = line[(colon + 1)..],
            QuotedPrintable = quotedPrintable,
            Base64 = base64,
            Charset = charset,
        };
        property.Types.AddRange(types);
        foreach (var (key, value) in parameters)
        {
            property.Parameters[key] = value;
        }

        return property;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static int IndexOutsideQuotes(ReadOnlySpan<char> text, char target)
    {
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                quoted = !quoted;
            }
            else if (text[index] == target && !quoted)
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string> SplitOutsideQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var quoted = false;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                quoted = !quoted;
            }
            else if (text[index] == separator && !quoted)
            {
                parts.Add(text[start..index]);
                start = index + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    // ---- Values ----------------------------------------------------------------------------------

    /// <summary>The raw value split on <paramref name="separator"/> where it is not escaped; each part still escaped and encoded.</summary>
    private static List<string> SplitUnescaped(string raw, char separator)
    {
        var parts = new List<string>();
        var start = 0;
        for (var index = 0; index < raw.Length; index++)
        {
            if (raw[index] == '\\' && index + 1 < raw.Length)
            {
                index++;
            }
            else if (raw[index] == separator)
            {
                parts.Add(raw[start..index]);
                start = index + 1;
            }
        }

        parts.Add(raw[start..]);
        return parts;
    }

    /// <summary>Quoted-printable decoding (when the property says so), then unescaping.</summary>
    private static string Decode(Property property, string raw, bool oldVersion)
    {
        var value = property.QuotedPrintable ? DecodeQuotedPrintable(raw, property.Charset) : raw;
        return Unescape(value, oldVersion);
    }

    private static string DecodeQuotedPrintable(string raw, Encoding? charset)
    {
        if (!raw.Contains('=', StringComparison.Ordinal))
        {
            return raw;
        }

        var builder = new StringBuilder(raw.Length);
        var pending = new List<byte>();
        for (var index = 0; index < raw.Length; index++)
        {
            var character = raw[index];
            if (character == '=' && index + 2 < raw.Length && IsHex(raw[index + 1]) && IsHex(raw[index + 2]))
            {
                pending.Add((byte)((HexValue(raw[index + 1]) << 4) | HexValue(raw[index + 2])));
                index += 2;
                continue;
            }

            Flush(builder, pending, charset);
            builder.Append(character);
        }

        Flush(builder, pending, charset);
        return builder.ToString();
    }

    private static void Flush(StringBuilder builder, List<byte> pending, Encoding? charset)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var bytes = pending.ToArray();
        pending.Clear();
        if (charset is not null && charset is not UTF8Encoding)
        {
            builder.Append(charset.GetString(bytes));
            return;
        }

        try
        {
            builder.Append(StrictUtf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            builder.Append(TextDecoder.Windows1252().GetString(bytes));
        }
    }

    private static bool IsHex(char character) => character is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');

    private static int HexValue(char character) =>
        character <= '9' ? character - '0' : (character | 0x20) - 'a' + 10;

    private static string Unescape(string value, bool oldVersion)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index + 1 >= value.Length)
            {
                builder.Append(character);
                continue;
            }

            var next = value[index + 1];
            if (oldVersion)
            {
                // vCard 2.1 escapes only the semicolon.
                if (next == ';')
                {
                    builder.Append(';');
                    index++;
                }
                else
                {
                    builder.Append(character);
                }

                continue;
            }

            switch (next)
            {
                case 'n' or 'N':
                    builder.Append('\n');
                    index++;
                    break;
                case ',' or ';' or '\\':
                    builder.Append(next);
                    index++;
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string Single(Property property, bool oldVersion) => Decode(property, property.Raw, oldVersion).Trim();

    private static List<string> Parts(Property property, char separator, bool oldVersion) =>
        SplitUnescaped(property.Raw, separator).Select(part => Decode(property, part, oldVersion).Trim()).ToList();

    // ---- Mapping ---------------------------------------------------------------------------------

    private static ImportPersonDraft ToDraft(int rowNumber, List<Property> properties)
    {
        var version = properties.FirstOrDefault(p => p.Name.Equals("VERSION", StringComparison.OrdinalIgnoreCase));
        var oldVersion = version is not null && version.Raw.Trim().StartsWith('2');

        var groupLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties.Where(p => p.Group is not null && p.Name.Equals("X-ABLABEL", StringComparison.OrdinalIgnoreCase) && !p.Base64))
        {
            groupLabels.TryAdd(property.Group!, Single(property, oldVersion));
        }

        string? first = null;
        string? last = null;
        string? full = null;
        string? nickname = null;
        ImportBirthdayDraft? birthday = null;
        var birthdayUnreadable = false;
        var birthdaySeen = false;
        var notes = new List<string>();
        var contacts = new List<ImportContactDraft>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in properties.Where(p => !p.Base64))
        {
            var name = property.Name.ToUpperInvariant();
            switch (name)
            {
                case "N" when first is null && last is null:
                    (first, last) = ReadName(property, oldVersion);
                    break;

                case "FN" when full is null:
                    full = Single(property, oldVersion);
                    break;

                case "NICKNAME" when nickname is null:
                    nickname = FirstOrNull(Parts(property, ',', oldVersion));
                    break;

                case "BDAY" when !birthdaySeen:
                    {
                        var value = Single(property, oldVersion);
                        if (value.Length == 0)
                        {
                            break;
                        }

                        birthdaySeen = true;
                        var isText = property.Parameters.TryGetValue("VALUE", out var kind) && kind.Equals("text", StringComparison.OrdinalIgnoreCase);
                        property.Parameters.TryGetValue("X-APPLE-OMIT-YEAR", out var omitYear);
                        if (!isText && BirthdayParser.TryParseVCard(value, omitYear, out var parsed))
                        {
                            birthday = parsed;
                        }
                        else
                        {
                            birthdayUnreadable = true;
                        }

                        break;
                    }

                case "NOTE":
                    {
                        var note = Single(property, oldVersion).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
                        if (note.Length > 0)
                        {
                            notes.Add(note);
                        }

                        break;
                    }

                case "EMAIL":
                    AddContact(contacts, seen, ContactMethodKind.Email, VCardLabels.From(ContactMethodKind.Email, property.Types, GroupLabel(property, groupLabels)), Single(property, oldVersion));
                    break;

                case "TEL":
                    {
                        if (property.Types.Contains("fax"))
                        {
                            break;
                        }

                        var value = Single(property, oldVersion);
                        if (value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                        {
                            value = value[4..];
                            var semicolon = value.IndexOf(';');
                            if (semicolon >= 0)
                            {
                                value = value[..semicolon];
                            }
                        }

                        AddContact(contacts, seen, ContactMethodKind.Phone, VCardLabels.From(ContactMethodKind.Phone, property.Types, GroupLabel(property, groupLabels)), value);
                        break;
                    }

                case "ADR":
                    AddContact(contacts, seen, ContactMethodKind.Address, VCardLabels.From(ContactMethodKind.Address, property.Types, GroupLabel(property, groupLabels)), ReadAddress(property, oldVersion));
                    break;

                case "X-SOCIALPROFILE":
                    {
                        var user = property.Parameters.GetValueOrDefault("x-user");
                        var value = string.IsNullOrWhiteSpace(user) ? Single(property, oldVersion) : user.Trim();
                        var network = property.Types.FirstOrDefault();
                        var label = network is { Length: > 0 }
                            ? char.ToUpperInvariant(network[0]) + network[1..]
                            : VCardLabels.From(ContactMethodKind.Social, property.Types, GroupLabel(property, groupLabels));
                        AddContact(contacts, seen, ContactMethodKind.Social, label is { Length: <= ContactMethod.LabelMaxLength } ? label : null, value);
                        break;
                    }
            }
        }

        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last) && !string.IsNullOrWhiteSpace(full))
        {
            (first, last) = NameSplitter.Split(full);
        }

        return new ImportPersonDraft(
            rowNumber,
            NullIfBlank(first),
            NullIfBlank(last),
            NullIfBlank(nickname),
            birthday,
            birthdayUnreadable,
            notes.Count == 0 ? null : string.Join("\n\n", notes),
            contacts);
    }

    private static string? GroupLabel(Property property, Dictionary<string, string> groupLabels) =>
        property.Group is not null && groupLabels.TryGetValue(property.Group, out var label) ? label : null;

    /// <summary>N is family;given;additional;prefix;suffix. Prefix and suffix are dropped; given and additional names join into the first name.</summary>
    private static (string? First, string? Last) ReadName(Property property, bool oldVersion)
    {
        var parts = SplitUnescaped(property.Raw, ';');
        string Part(int index) => index < parts.Count
            ? string.Join(' ', SplitUnescaped(parts[index], ',').Select(piece => Decode(property, piece, oldVersion).Trim()).Where(piece => piece.Length > 0))
            : string.Empty;

        var family = Part(0);
        var given = string.Join(' ', new[] { Part(1), Part(2) }.Where(piece => piece.Length > 0));

        // Only a family name: it is what the person is called, so it becomes the first name.
        return given.Length == 0 ? (NullIfBlank(family), null) : (given, NullIfBlank(family));
    }

    private static string ReadAddress(Property property, bool oldVersion)
    {
        if (property.Parameters.TryGetValue("LABEL", out var label) && !string.IsNullOrWhiteSpace(label))
        {
            return Unescape(label, oldVersion: false).Trim();
        }

        // Post office box, extended address, street, locality, region, postal code, country.
        return string.Join('\n', Parts(property, ';', oldVersion).Where(part => part.Length > 0));
    }

    private static void AddContact(List<ImportContactDraft> contacts, HashSet<string> seen, ContactMethodKind kind, string? label, string value)
    {
        value = ContactMethodRules.NormalizeValue(kind, value);
        if (value.Length == 0)
        {
            return;
        }

        var key = ContactMethodRules.ToNormalizedValue(kind, value);
        if (key.Length == 0)
        {
            key = value.ToLowerInvariant();
        }

        if (!seen.Add($"{(int)kind}:{key}"))
        {
            return;
        }

        contacts.Add(new ImportContactDraft(kind, label, value));
    }

    private static string? FirstOrNull(IEnumerable<string> values) => values.FirstOrDefault(value => value.Length > 0);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
