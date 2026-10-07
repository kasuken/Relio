namespace Relio.Application.People.Import;

/// <summary>What a CSV column is imported as.</summary>
public enum CsvField
{
    /// <summary>The column is not imported.</summary>
    Ignore,

    /// <summary>The first name.</summary>
    FirstName,

    /// <summary>A middle name, added after the first name.</summary>
    MiddleName,

    /// <summary>The last name.</summary>
    LastName,

    /// <summary>One column holding the whole name; used only when no first or last name is mapped.</summary>
    FullName,

    /// <summary>The nickname.</summary>
    Nickname,

    /// <summary>The birthday.</summary>
    Birthday,

    /// <summary>An email address.</summary>
    Email,

    /// <summary>A phone number.</summary>
    Phone,

    /// <summary>A postal address.</summary>
    Address,

    /// <summary>Notes, imported as the person's details.</summary>
    Notes,

    /// <summary>A column holding the label of another column ("Home", "Work"). Set by presets; never offered to the user.</summary>
    Label,
}

/// <summary>How one column is read.</summary>
/// <param name="ColumnIndex">The zero-based column.</param>
/// <param name="Field">What the column is imported as.</param>
/// <param name="FixedLabel">A label every value gets ("Mobile"), or <see langword="null"/>.</param>
/// <param name="LabelColumnIndex">The column holding this column's label, or <see langword="null"/>.</param>
public sealed record CsvColumnAssignment(int ColumnIndex, CsvField Field, string? FixedLabel = null, int? LabelColumnIndex = null);

/// <summary>Several columns that together make one postal address (Outlook's "Home Street", "Home City", ...).</summary>
/// <param name="Label">The address's label, or <see langword="null"/>.</param>
/// <param name="ColumnIndexes">The columns, in the order their parts are joined.</param>
/// <param name="Enabled">Whether the block is imported.</param>
public sealed record CsvAddressBlock(string? Label, IReadOnlyList<int> ColumnIndexes, bool Enabled = true);

/// <summary>
/// How a CSV table's columns become people: one assignment per column, the address blocks, and the order
/// birthdays are written in.
/// </summary>
public sealed record CsvColumnMapping(IReadOnlyList<CsvColumnAssignment> Columns, IReadOnlyList<CsvAddressBlock> AddressBlocks, DateOrder DateOrder)
{
    /// <summary>Whether a first name or a full name column is mapped (a person needs a name).</summary>
    public bool HasName => Columns.Any(column => column.Field is CsvField.FirstName or CsvField.FullName);

    /// <summary>Whether a birthday column is mapped.</summary>
    public bool HasBirthday => Columns.Any(column => column.Field == CsvField.Birthday);

    /// <summary>The columns that hold birthdays.</summary>
    public IEnumerable<int> BirthdayColumns => Columns.Where(column => column.Field == CsvField.Birthday).Select(column => column.ColumnIndex);

    /// <summary>A copy with column <paramref name="columnIndex"/> imported as <paramref name="field"/>. Its label link is kept only while the field stays the same.</summary>
    public CsvColumnMapping WithField(int columnIndex, CsvField field) => this with
    {
        Columns = [.. Columns.Select(column => column.ColumnIndex != columnIndex
            ? column
            : column.Field == field ? column : new CsvColumnAssignment(columnIndex, field))],
    };

    /// <summary>A copy with address block <paramref name="blockIndex"/> switched on or off.</summary>
    public CsvColumnMapping WithAddressBlock(int blockIndex, bool enabled) => this with
    {
        AddressBlocks = [.. AddressBlocks.Select((block, index) => index == blockIndex ? block with { Enabled = enabled } : block)],
    };

    /// <summary>A copy with another date order.</summary>
    public CsvColumnMapping WithDateOrder(DateOrder order) => this with { DateOrder = order };
}

/// <summary>The kind of file the headers looked like.</summary>
public enum CsvPreset
{
    /// <summary>Google Contacts' current CSV export.</summary>
    Google,

    /// <summary>Google Contacts' older CSV export (Given Name, Family Name).</summary>
    GoogleLegacy,

    /// <summary>Outlook's contacts export.</summary>
    Outlook,

    /// <summary>Anything else: columns matched by common names.</summary>
    Generic,
}

/// <summary>What <see cref="CsvMappingPresets.Suggest"/> came up with.</summary>
/// <param name="Preset">The kind of file the headers looked like.</param>
/// <param name="Mapping">The suggested mapping.</param>
/// <param name="DateOrderIsAmbiguous">Whether birthdays are written like 03/04/1990 with nothing in the file saying which part is the month.</param>
public sealed record CsvMappingSuggestion(CsvPreset Preset, CsvColumnMapping Mapping, bool DateOrderIsAmbiguous);
