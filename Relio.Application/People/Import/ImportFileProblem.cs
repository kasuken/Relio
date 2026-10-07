namespace Relio.Application.People.Import;

/// <summary>Why a file could not be imported at all. Codes, not messages: <c>Relio.Web</c> words them.</summary>
public enum ImportFileProblem
{
    /// <summary>The file has no content.</summary>
    Empty,

    /// <summary>The file is larger than <see cref="ImportLimits.MaxFileBytes"/>.</summary>
    TooLarge,

    /// <summary>The file is neither a vCard nor a CSV file of contacts.</summary>
    NotContacts,

    /// <summary>The browser could not deliver the file.</summary>
    Unreadable,

    /// <summary>The file was read, but holds no people.</summary>
    NoPeople,
}
