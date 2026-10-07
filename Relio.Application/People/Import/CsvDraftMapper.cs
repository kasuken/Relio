using Relio.Domain;

namespace Relio.Application.People.Import;

/// <summary>
/// Turns the records of a <see cref="CsvTable"/> into <see cref="ImportPersonDraft"/>s using a
/// <see cref="CsvColumnMapping"/>. Pure. A record with nothing in any mapped column is skipped (and counted).
/// </summary>
public static class CsvDraftMapper
{
    private const string GoogleSeparator = " ::: ";

    /// <summary>Maps every record of <paramref name="table"/>.</summary>
    public static ImportReadResult Map(CsvTable table, CsvColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(mapping);

        var drafts = new List<ImportPersonDraft>();
        var skipped = 0;
        foreach (var record in table.Records)
        {
            var draft = MapRecord(record, mapping);
            if (draft.IsEmpty)
            {
                skipped++;
            }
            else
            {
                drafts.Add(draft);
            }
        }

        // The table holds at most MaxPeople records; a longer file says so through its own totals.
        return new ImportReadResult(drafts, table.TotalFound - skipped, table.Truncated, skipped);
    }

    private static ImportPersonDraft MapRecord(CsvRecord record, CsvColumnMapping mapping)
    {
        string Cell(int index) => index >= 0 && index < record.Cells.Count ? record.Cells[index].Trim() : string.Empty;

        var firsts = new List<string>();
        var middles = new List<string>();
        var lasts = new List<string>();
        string? full = null;
        string? nickname = null;
        string? birthdayCell = null;
        var notes = new List<string>();
        var positioned = new List<(int Position, ImportContactDraft Contact)>();

        foreach (var column in mapping.Columns)
        {
            var value = Cell(column.ColumnIndex);
            if (value.Length == 0)
            {
                continue;
            }

            switch (column.Field)
            {
                case CsvField.FirstName:
                    firsts.Add(value);
                    break;
                case CsvField.MiddleName:
                    middles.Add(value);
                    break;
                case CsvField.LastName:
                    lasts.Add(value);
                    break;
                case CsvField.FullName:
                    full ??= value;
                    break;
                case CsvField.Nickname:
                    nickname ??= value;
                    break;
                case CsvField.Birthday:
                    birthdayCell ??= value;
                    break;
                case CsvField.Notes:
                    notes.Add(value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
                    break;
                case CsvField.Email or CsvField.Phone or CsvField.Address:
                    AddMultiValued(positioned, column, value, Cell(column.LabelColumnIndex ?? -1));
                    break;
            }
        }

        foreach (var block in mapping.AddressBlocks.Where(block => block.Enabled && block.ColumnIndexes.Count > 0))
        {
            var parts = block.ColumnIndexes.Select(Cell).Where(part => part.Length > 0).ToList();
            if (parts.Count > 0)
            {
                positioned.Add((block.ColumnIndexes.Min(), new ImportContactDraft(ContactMethodKind.Address, ImportLabels.FromCsv(block.Label), ContactMethodRules.NormalizeValue(ContactMethodKind.Address, string.Join('\n', parts)))));
            }
        }

        var first = string.Join(' ', firsts.Concat(middles));
        var last = string.Join(' ', lasts);
        if (first.Length == 0 && last.Length == 0 && full is not null)
        {
            (var splitFirst, var splitLast) = NameSplitter.Split(full);
            first = splitFirst ?? string.Empty;
            last = splitLast ?? string.Empty;
        }

        ImportBirthdayDraft? birthday = null;
        var unreadable = false;
        if (birthdayCell is not null && !BirthdayParser.TryParseCsv(birthdayCell, mapping.DateOrder, out birthday))
        {
            unreadable = true;
        }

        return new ImportPersonDraft(
            record.RecordNumber,
            first.Length == 0 ? null : first,
            last.Length == 0 ? null : last,
            nickname,
            birthday,
            unreadable,
            notes.Count == 0 ? null : string.Join("\n\n", notes),
            [.. positioned.OrderBy(item => item.Position).Select(item => item.Contact)]);
    }

    /// <summary>Adds one contact per value of a cell; Google joins several values with " ::: ", and a label cell does too.</summary>
    private static void AddMultiValued(List<(int, ImportContactDraft)> into, CsvColumnAssignment column, string cell, string labelCell)
    {
        var kind = column.Field switch
        {
            CsvField.Email => ContactMethodKind.Email,
            CsvField.Phone => ContactMethodKind.Phone,
            _ => ContactMethodKind.Address,
        };

        var values = cell.Split(GoogleSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var labels = labelCell.Split(GoogleSeparator, StringSplitOptions.TrimEntries);

        for (var index = 0; index < values.Length; index++)
        {
            var rawLabel = index < labels.Length ? labels[index] : null;
            rawLabel = string.IsNullOrWhiteSpace(rawLabel) ? column.FixedLabel : rawLabel;
            if (kind == ContactMethodKind.Phone && rawLabel?.Contains("fax", StringComparison.OrdinalIgnoreCase) == true)
            {
                continue;
            }

            into.Add((column.ColumnIndex, new ImportContactDraft(kind, ImportLabels.FromCsv(rawLabel), ContactMethodRules.NormalizeValue(kind, values[index]))));
        }
    }
}

/// <summary>Cleans up the labels CSV exports carry.</summary>
internal static class ImportLabels
{
    /// <summary>
    /// A label for a contact detail: a leading "* " (Google's system labels) removed, "Other" meaning no
    /// label, and anything longer than <see cref="ContactMethod.LabelMaxLength"/> dropped.
    /// </summary>
    public static string? FromCsv(string? raw)
    {
        var label = raw?.Trim();
        if (label is { Length: > 0 } && label.StartsWith('*'))
        {
            label = label.TrimStart('*').Trim();
        }

        if (string.IsNullOrEmpty(label) || label.Equals("Other", StringComparison.OrdinalIgnoreCase) || label.Length > ContactMethod.LabelMaxLength)
        {
            return null;
        }

        return label;
    }
}
