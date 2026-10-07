namespace Relio.Application.People.Import;

/// <summary>
/// The bounds that keep an import small enough to read, preview and save from a long-lived
/// interactive circuit (issue #29). Every parser and the page enforce them; none can be raised by
/// the file.
/// </summary>
public static class ImportLimits
{
    /// <summary>The largest file Relio reads: 1 MB. A phone's whole address book is usually well under it.</summary>
    public const int MaxFileBytes = 1_048_576;

    /// <summary>The most people one import reads. A file with more keeps its first 2,000 and says so.</summary>
    public const int MaxPeople = 2_000;

    /// <summary>The most columns a CSV file may have; more means it is not a contacts file.</summary>
    public const int MaxCsvColumns = 200;

    /// <summary>The longest CSV cell kept, in characters. A longer cell is cut, and the rules then drop the field as too long.</summary>
    public const int MaxCsvCellLength = 20_000;

    /// <summary>The longest vCard line read, in characters. A longer line (an embedded photo) is skipped.</summary>
    public const int MaxVCardLineLength = 200_000;
}
