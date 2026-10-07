using System.Globalization;

namespace Relio.Application.People.Import;

/// <summary>What was found out about the way a file writes its birthdays.</summary>
/// <param name="Decisive">The order the dates prove (a day above 12, or ISO dates), or <see langword="null"/> when they prove nothing.</param>
/// <param name="HasWrittenDates">Whether any date is written as <c>a/b/yyyy</c>, <c>a.b.yyyy</c> or <c>a-b-yyyy</c>.</param>
public sealed record DateOrderEvidence(DateOrder? Decisive, bool HasWrittenDates)
{
    /// <summary>Whether the file writes dates the user has to say the order of.</summary>
    public bool IsAmbiguous => Decisive is null && HasWrittenDates;
}

/// <summary>
/// Suggests how a CSV file's columns map to a person: recognises the headers of Google Contacts' two
/// exports and Outlook's, and otherwise matches common column names. Pure; only the header row and the
/// birthday cells are looked at. The user always sees and can change the suggestion.
/// </summary>
public static class CsvMappingPresets
{
    private static readonly Dictionary<string, (CsvField Field, string? Label)> GenericNames = BuildGenericNames();

    /// <summary>Suggests a mapping for <paramref name="table"/>.</summary>
    public static CsvMappingSuggestion Suggest(CsvTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var headers = new HeaderIndex(table.Headers);
        var (preset, assignments, blocks) = headers.Has("First Name") && (headers.Has("E-mail 1 - Value") || headers.Has("Phone 1 - Value"))
            ? (CsvPreset.Google, Google(headers, " - Label"), [])
            : headers.Has("Given Name") && headers.Has("Family Name")
                ? (CsvPreset.GoogleLegacy, GoogleLegacy(headers), [])
                : headers.Has("First Name") && headers.Has("E-mail Address")
                    ? OutlookSuggestion(headers)
                    : (CsvPreset.Generic, Generic(headers), (IReadOnlyList<CsvAddressBlock>)[]);

        var columns = Complete(table.Headers.Count, assignments);
        var birthdayColumns = columns.Where(column => column.Field == CsvField.Birthday).Select(column => column.ColumnIndex).ToList();
        var evidence = DetectDateOrder(table, birthdayColumns);

        return new CsvMappingSuggestion(preset, new CsvColumnMapping(columns, blocks, ChooseDateOrder(preset, evidence)), evidence.IsAmbiguous);
    }

    /// <summary>
    /// The order to read dates in: the one the dates prove, else month first for Outlook (a US export) and day
    /// first for everything else when dates are written out, else year first (ISO dates, which need no order).
    /// </summary>
    public static DateOrder ChooseDateOrder(CsvPreset preset, DateOrderEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return evidence.Decisive
            ?? (evidence.HasWrittenDates
                ? preset == CsvPreset.Outlook ? DateOrder.MonthDayYear : DateOrder.DayMonthYear
                : DateOrder.YearMonthDay);
    }

    /// <summary>
    /// Looks at the birthday cells of <paramref name="birthdayColumns"/>: a day above 12 proves day-month-year,
    /// a month slot above 12 proves month-day-year, ISO dates prove year-month-day, and dates like 03/04/1990
    /// prove nothing.
    /// </summary>
    public static DateOrderEvidence DetectDateOrder(CsvTable table, IEnumerable<int> birthdayColumns)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(birthdayColumns);

        var columns = birthdayColumns.ToList();
        int dayFirst = 0, monthFirst = 0, either = 0, iso = 0;

        foreach (var record in table.Records)
        {
            foreach (var column in columns)
            {
                var cell = column < record.Cells.Count ? record.Cells[column].Trim() : string.Empty;
                if (cell.Length == 0)
                {
                    continue;
                }

                if (cell.StartsWith("--", StringComparison.Ordinal))
                {
                    iso++;
                    continue;
                }

                if (!BirthdayParser.TrySplitDate(cell, out var first, out var second, out _, out var yearFirst))
                {
                    continue;
                }

                if (yearFirst)
                {
                    iso++;
                }
                else if (first > 12 && second <= 12)
                {
                    dayFirst++;
                }
                else if (second > 12 && first <= 12)
                {
                    monthFirst++;
                }
                else
                {
                    either++;
                }
            }
        }

        DateOrder? decisive = null;
        if (dayFirst > 0 && dayFirst >= monthFirst)
        {
            decisive = DateOrder.DayMonthYear;
        }
        else if (monthFirst > 0)
        {
            decisive = DateOrder.MonthDayYear;
        }
        else if (iso > 0 && either == 0)
        {
            decisive = DateOrder.YearMonthDay;
        }

        return new DateOrderEvidence(decisive, dayFirst + monthFirst + either > 0);
    }

    // ---- Presets ---------------------------------------------------------------------------------

    private static List<CsvColumnAssignment> Google(HeaderIndex headers, string labelSuffix)
    {
        var assignments = new List<CsvColumnAssignment>();
        Plain(headers, assignments, "First Name", CsvField.FirstName);
        Plain(headers, assignments, "Middle Name", CsvField.MiddleName);
        Plain(headers, assignments, "Last Name", CsvField.LastName);
        Plain(headers, assignments, "Nickname", CsvField.Nickname);
        Plain(headers, assignments, "Birthday", CsvField.Birthday);
        Plain(headers, assignments, "Notes", CsvField.Notes);
        Numbered(headers, assignments, "E-mail ", " - Value", labelSuffix, CsvField.Email);
        Numbered(headers, assignments, "Phone ", " - Value", labelSuffix, CsvField.Phone);
        Numbered(headers, assignments, "Address ", " - Formatted", labelSuffix, CsvField.Address);
        return assignments;
    }

    private static List<CsvColumnAssignment> GoogleLegacy(HeaderIndex headers)
    {
        var assignments = new List<CsvColumnAssignment>();
        Plain(headers, assignments, "Given Name", CsvField.FirstName);
        Plain(headers, assignments, "Additional Name", CsvField.MiddleName);
        Plain(headers, assignments, "Family Name", CsvField.LastName);
        Plain(headers, assignments, "Nickname", CsvField.Nickname);
        Plain(headers, assignments, "Birthday", CsvField.Birthday);
        Plain(headers, assignments, "Notes", CsvField.Notes);
        Numbered(headers, assignments, "E-mail ", " - Value", " - Type", CsvField.Email);
        Numbered(headers, assignments, "Phone ", " - Value", " - Type", CsvField.Phone);
        Numbered(headers, assignments, "Address ", " - Formatted", " - Type", CsvField.Address);
        return assignments;
    }

    private static (CsvPreset, List<CsvColumnAssignment>, IReadOnlyList<CsvAddressBlock>) OutlookSuggestion(HeaderIndex headers)
    {
        var assignments = new List<CsvColumnAssignment>();
        Plain(headers, assignments, "First Name", CsvField.FirstName);
        Plain(headers, assignments, "Middle Name", CsvField.MiddleName);
        Plain(headers, assignments, "Last Name", CsvField.LastName);
        Plain(headers, assignments, "Nickname", CsvField.Nickname);
        Plain(headers, assignments, "Birthday", CsvField.Birthday);
        Plain(headers, assignments, "Notes", CsvField.Notes);

        foreach (var email in new[] { "E-mail Address", "E-mail 2 Address", "E-mail 3 Address" })
        {
            Plain(headers, assignments, email, CsvField.Email);
        }

        Labelled(headers, assignments, "Mobile Phone", "Mobile");
        Labelled(headers, assignments, "Home Phone", "Home");
        Labelled(headers, assignments, "Home Phone 2", "Home");
        Labelled(headers, assignments, "Business Phone", "Work");
        Labelled(headers, assignments, "Business Phone 2", "Work");
        Labelled(headers, assignments, "Primary Phone", "Main");
        Labelled(headers, assignments, "Other Phone", null);

        var blocks = new List<CsvAddressBlock>();
        foreach (var (prefix, label) in new (string, string?)[] { ("Home", "Home"), ("Business", "Work"), ("Other", null) })
        {
            var indexes = new[] { " Street", " Street 2", " Street 3", " City", " State", " Postal Code", " Country/Region" }
                .Select(suffix => headers.IndexOf(prefix + suffix))
                .Where(index => index >= 0)
                .ToList();
            if (indexes.Count > 0)
            {
                blocks.Add(new CsvAddressBlock(label, indexes));
            }
        }

        return (CsvPreset.Outlook, assignments, blocks);
    }

    private static List<CsvColumnAssignment> Generic(HeaderIndex headers)
    {
        var assignments = new List<CsvColumnAssignment>();
        for (var index = 0; index < headers.Count; index++)
        {
            var key = NormalizeHeader(headers[index]);
            if (GenericNames.TryGetValue(key, out var match))
            {
                assignments.Add(new CsvColumnAssignment(index, match.Field, match.Label));
            }
        }

        return assignments;
    }

    private static void Plain(HeaderIndex headers, List<CsvColumnAssignment> assignments, string header, CsvField field)
    {
        var index = headers.IndexOf(header);
        if (index >= 0)
        {
            assignments.Add(new CsvColumnAssignment(index, field));
        }
    }

    private static void Labelled(HeaderIndex headers, List<CsvColumnAssignment> assignments, string header, string? label)
    {
        var index = headers.IndexOf(header);
        if (index >= 0)
        {
            assignments.Add(new CsvColumnAssignment(index, CsvField.Phone, label));
        }
    }

    /// <summary>Pairs "E-mail 2 - Value" with "E-mail 2 - Label" (or "Type").</summary>
    private static void Numbered(HeaderIndex headers, List<CsvColumnAssignment> assignments, string prefix, string valueSuffix, string labelSuffix, CsvField field)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index];
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !header.EndsWith(valueSuffix, StringComparison.OrdinalIgnoreCase)
                || header.Length <= prefix.Length + valueSuffix.Length)
            {
                continue;
            }

            var number = header[prefix.Length..^valueSuffix.Length];
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var labelIndex = headers.IndexOf(prefix + number + labelSuffix);
            assignments.Add(new CsvColumnAssignment(index, field, null, labelIndex >= 0 ? labelIndex : null));
            if (labelIndex >= 0)
            {
                assignments.Add(new CsvColumnAssignment(labelIndex, CsvField.Label));
            }
        }
    }

    /// <summary>One assignment per column, in column order; columns nothing claimed are ignored.</summary>
    private static List<CsvColumnAssignment> Complete(int width, List<CsvColumnAssignment> assignments)
    {
        var byColumn = new Dictionary<int, CsvColumnAssignment>();
        foreach (var assignment in assignments)
        {
            byColumn.TryAdd(assignment.ColumnIndex, assignment);
        }

        return Enumerable.Range(0, width)
            .Select(index => byColumn.TryGetValue(index, out var assignment) ? assignment : new CsvColumnAssignment(index, CsvField.Ignore))
            .ToList();
    }

    private static string NormalizeHeader(string header) =>
        string.Join(' ', header.Trim().ToLowerInvariant().Replace('_', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<string, (CsvField, string?)> BuildGenericNames()
    {
        var names = new Dictionary<string, (CsvField, string?)>(StringComparer.Ordinal);

        void Add(CsvField field, string? label, params string[] synonyms)
        {
            foreach (var synonym in synonyms)
            {
                names[synonym] = (field, label);
            }
        }

        Add(CsvField.FirstName, null, "first name", "firstname", "given name", "first");
        Add(CsvField.MiddleName, null, "middle name", "middlename");
        Add(CsvField.LastName, null, "last name", "lastname", "surname", "family name", "last");
        Add(CsvField.FullName, null, "name", "full name", "fullname", "display name");
        Add(CsvField.Nickname, null, "nickname", "nick name");
        Add(CsvField.Email, null, "email", "e-mail", "email address", "e-mail address", "mail");
        Add(CsvField.Phone, null, "phone", "telephone", "tel", "phone number");
        Add(CsvField.Phone, "Mobile", "mobile", "cell", "cell phone", "mobile phone");
        Add(CsvField.Birthday, null, "birthday", "birth date", "birthdate", "date of birth", "dob");
        Add(CsvField.Notes, null, "notes", "note", "comments", "comment");
        Add(CsvField.Address, null, "address", "postal address");
        return names;
    }

    /// <summary>Header lookups, trimmed and ignoring case; the first of two equal headers wins.</summary>
    private sealed class HeaderIndex
    {
        private readonly List<string> _headers;
        private readonly Dictionary<string, int> _byName = new(StringComparer.OrdinalIgnoreCase);

        public HeaderIndex(IReadOnlyList<string> headers)
        {
            _headers = [.. headers.Select(header => header.Trim())];
            for (var index = 0; index < _headers.Count; index++)
            {
                _byName.TryAdd(_headers[index], index);
            }
        }

        public int Count => _headers.Count;

        public string this[int index] => _headers[index];

        public bool Has(string header) => _byName.ContainsKey(header);

        public int IndexOf(string header) => _byName.GetValueOrDefault(header, -1);
    }
}
