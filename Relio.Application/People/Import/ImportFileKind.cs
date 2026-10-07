namespace Relio.Application.People.Import;

/// <summary>The kind of contacts file an import reads.</summary>
public enum ImportFileKind
{
    /// <summary>A vCard (.vcf) file: versions 2.1, 3.0 and 4.0.</summary>
    VCard,

    /// <summary>A delimited text (.csv, .tsv) file with a header row.</summary>
    Csv,
}
