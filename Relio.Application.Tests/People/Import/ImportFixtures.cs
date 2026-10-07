using Relio.Application.People.Import;
using Relio.Domain;

namespace Relio.Application.Tests.People.Import;

/// <summary>
/// The import fixtures: synthetic files (fictional people, example.com addresses, numbers in the
/// ranges reserved for fiction) kept byte-exact by <c>.gitattributes</c> and <c>.editorconfig</c>.
/// </summary>
internal static class ImportFixtures
{
    public static byte[] Bytes(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "People", "Import", "Fixtures", name));

    public static ImportReadResult VCard(string name) => VCardReader.Read(Bytes(name));

    public static CsvTable Csv(string name) => CsvReader.Read(Bytes(name), name);

    /// <summary>Reads a CSV fixture and maps it with the suggested mapping.</summary>
    public static (CsvMappingSuggestion Suggestion, ImportReadResult Read) MappedCsv(string name)
    {
        var table = Csv(name);
        var suggestion = CsvMappingPresets.Suggest(table);
        return (suggestion, CsvDraftMapper.Map(table, suggestion.Mapping));
    }

    public static ImportPersonDraft Person(this ImportReadResult read, string firstName) =>
        read.People.Single(person => person.FirstName == firstName);

    public static ImportContactDraft Contact(this ImportPersonDraft person, ContactMethodKind kind, int index = 0) =>
        person.ContactMethods.Where(contact => contact.Kind == kind).ElementAt(index);

    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);
}
