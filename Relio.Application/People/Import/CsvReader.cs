using System.Text;

namespace Relio.Application.People.Import;

/// <summary>One data record of a CSV file. Personal data: never log it.</summary>
/// <param name="RecordNumber">The record's number in the file, counting the header as 1 (blank records count too, so it matches a spreadsheet's row).</param>
/// <param name="Cells">The cells, padded or cut to the header's width.</param>
public sealed record CsvRecord(int RecordNumber, IReadOnlyList<string> Cells);

/// <summary>A parsed CSV file: its header row and its data records. Personal data: never log it.</summary>
/// <param name="Delimiter">The delimiter that was detected (or implied by <c>.tsv</c>).</param>
/// <param name="Encoding">The encoding the file was decoded with.</param>
/// <param name="Headers">The header row, trimmed; an empty header is named "Column N".</param>
/// <param name="Records">The non-blank data records, at most <see cref="ImportLimits.MaxPeople"/>.</param>
/// <param name="TotalFound">How many non-blank data records the file holds.</param>
/// <param name="Truncated">Whether the file held more than <see cref="ImportLimits.MaxPeople"/> records.</param>
public sealed record CsvTable(
    char Delimiter,
    ImportTextEncoding Encoding,
    IReadOnlyList<string> Headers,
    IReadOnlyList<CsvRecord> Records,
    int TotalFound,
    bool Truncated);

/// <summary>
/// A small RFC 4180 reader: a hand-rolled state machine with no regular expressions. Quoted fields
/// may hold delimiters, line breaks and doubled quotes; CRLF, LF and CR end a record; a quote inside an
/// unquoted field is literal; an unterminated quote runs to the end of the file. The delimiter
/// (comma, semicolon or tab) is detected from the first record. Pure: no I/O, no logging.
/// </summary>
public static class CsvReader
{
    /// <summary>Reads <paramref name="bytes"/> as CSV; <paramref name="fileName"/> only decides that <c>.tsv</c> is tab separated.</summary>
    /// <exception cref="ImportFileException">The file is empty or has more than <see cref="ImportLimits.MaxCsvColumns"/> columns (<see cref="ImportFileProblem.NotContacts"/>), or has no header row.</exception>
    public static CsvTable Read(ReadOnlySpan<byte> bytes, string? fileName)
    {
        var decoded = TextDecoder.Decode(bytes);
        var text = decoded.Text;

        // A NUL character is never in a text file: this is some other kind of file with a CSV name.
        if (text.AsSpan(0, Math.Min(text.Length, 1_024)).Contains('\0'))
        {
            throw new ImportFileException(ImportFileProblem.NotContacts);
        }

        var delimiter = string.Equals(Path.GetExtension(fileName ?? string.Empty), ".tsv", StringComparison.OrdinalIgnoreCase)
            ? '\t'
            : DetectDelimiter(text);

        List<string>? headers = null;
        var records = new List<CsvRecord>();
        var total = 0;
        var recordNumber = 0;

        foreach (var cells in ReadRecords(text, delimiter))
        {
            recordNumber++;
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (headers is null)
            {
                if (cells.Count > ImportLimits.MaxCsvColumns)
                {
                    throw new ImportFileException(ImportFileProblem.NotContacts);
                }

                headers = cells.Select((cell, index) => cell.Trim() is { Length: > 0 } header ? header : $"Column {index + 1}").ToList();
                continue;
            }

            total++;
            if (records.Count < ImportLimits.MaxPeople)
            {
                records.Add(new CsvRecord(recordNumber, Fit(cells, headers.Count)));
            }
        }

        if (headers is null)
        {
            throw new ImportFileException(ImportFileProblem.NoPeople);
        }

        return new CsvTable(delimiter, decoded.Encoding, headers, records, total, total > ImportLimits.MaxPeople);
    }

    private static List<string> Fit(List<string> cells, int width)
    {
        if (cells.Count == width)
        {
            return cells;
        }

        if (cells.Count > width)
        {
            return cells[..width];
        }

        var padded = new List<string>(cells);
        while (padded.Count < width)
        {
            padded.Add(string.Empty);
        }

        return padded;
    }

    /// <summary>Counts commas, semicolons and tabs outside quotes in the first record; the highest wins, ties go comma, semicolon, tab.</summary>
    private static char DetectDelimiter(string text)
    {
        int commas = 0, semicolons = 0, tabs = 0;
        var quoted = false;
        var sawContent = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
                sawContent = true;
            }
            else if (!quoted && character is '\r' or '\n')
            {
                if (sawContent)
                {
                    break;
                }
            }
            else if (!quoted)
            {
                sawContent = true;
                switch (character)
                {
                    case ',':
                        commas++;
                        break;
                    case ';':
                        semicolons++;
                        break;
                    case '\t':
                        tabs++;
                        break;
                }
            }
        }

        if (commas >= semicolons && commas >= tabs)
        {
            return ',';
        }

        return semicolons >= tabs ? ';' : '\t';
    }

    private static IEnumerable<List<string>> ReadRecords(string text, char delimiter)
    {
        var cells = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        var recordHasContent = false;

        void EndField()
        {
            cells.Add(field.ToString());
            field.Clear();
            wasQuoted = false;
        }

        void Append(char character)
        {
            // Overlong cells are cut here, so a huge cell never costs more memory than the limit.
            if (field.Length < ImportLimits.MaxCsvCellLength)
            {
                field.Append(character);
            }
        }

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            recordHasContent = true;

            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    Append(character);
                }

                continue;
            }

            if (character == '"' && !wasQuoted && IsBlank(field))
            {
                field.Clear();
                quoted = true;
                wasQuoted = true;
            }
            else if (character == delimiter)
            {
                EndField();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                EndField();
                yield return cells;
                cells = [];
                recordHasContent = false;
            }
            else
            {
                Append(character);
            }
        }

        if (recordHasContent)
        {
            EndField();
            yield return cells;
        }
    }

    private static bool IsBlank(StringBuilder builder)
    {
        for (var index = 0; index < builder.Length; index++)
        {
            if (!char.IsWhiteSpace(builder[index]))
            {
                return false;
            }
        }

        return true;
    }
}
