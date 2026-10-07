using System.Text;
using Relio.Application.People.Import;
using Relio.Domain;

namespace Relio.Application.Tests.People.Import;

public sealed class CsvReaderTests
{
    private static CsvTable Read(string text, string name = "a.csv") => CsvReader.Read(Encoding.UTF8.GetBytes(text), name);

    private static string[][] Rows(CsvTable table) => table.Records.Select(record => record.Cells.ToArray()).ToArray();

    [Fact]
    public void Reads_quoted_fields_with_delimiters_newlines_and_doubled_quotes()
    {
        var table = Read("a,b,c\r\n\"x,1\",\"line1\r\nline2\",\"say \"\"hi\"\"\"\r\n");

        Rows(table).Should().BeEquivalentTo(new[] { new[] { "x,1", "line1\r\nline2", "say \"hi\"" } }, o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("a,b,c\n1,2,3", ',')]
    [InlineData("a;b;c\n1;2;3", ';')]
    [InlineData("a\tb\tc\n1\t2\t3", '\t')]
    [InlineData("a;b,c;d\n1;2;3;4", ';')]
    [InlineData("a\n1", ',')]
    public void Detects_comma_semicolon_and_tab(string text, char delimiter)
    {
        Read(text).Delimiter.Should().Be(delimiter);
    }

    [Fact]
    public void Ignores_delimiters_inside_quotes_when_detecting()
    {
        Read("\"a;b;c\",d\n1,2").Delimiter.Should().Be(',');
    }

    [Fact]
    public void Uses_tab_for_tsv_files()
    {
        var table = Read("a,b\tc\n1,2\t3", "contacts.TSV");

        table.Delimiter.Should().Be('\t');
        table.Headers.Should().Equal("a,b", "c");
    }

    [Fact]
    public void Accepts_crlf_lf_and_cr()
    {
        foreach (var newline in new[] { "\r\n", "\n", "\r" })
        {
            var table = Read($"a,b{newline}1,2{newline}3,4{newline}");
            Rows(table).Should().HaveCount(2, $"newline {newline.Length} chars");
        }
    }

    [Fact]
    public void Skips_blank_records_and_pads_ragged_ones()
    {
        var table = Read("\r\na,b,c\r\n\r\n1\r\n 2 ,x,y,z\r\n,,\r\n");

        table.Headers.Should().Equal("a", "b", "c");
        table.Records.Select(record => record.RecordNumber).Should().Equal(4, 5);
        Rows(table).Should().BeEquivalentTo(new[] { new[] { "1", "", "" }, new[] { " 2 ", "x", "y" } }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Names_empty_headers()
    {
        Read("a,,c\n1,2,3").Headers.Should().Equal("a", "Column 2", "c");
    }

    [Fact]
    public void A_quote_inside_an_unquoted_field_is_literal()
    {
        Rows(Read("a,b\nsay \"hi\",x")).Single().Should().Equal("say \"hi\"", "x");
    }

    [Fact]
    public void An_unterminated_quote_runs_to_the_end()
    {
        var table = Read("a,b\n1,\"never closed\nstill going");

        Rows(table).Single().Should().Equal("1", "never closed\nstill going");
    }

    [Fact]
    public void Rejects_more_than_200_columns()
    {
        var header = string.Join(',', Enumerable.Range(0, ImportLimits.MaxCsvColumns + 1).Select(i => $"c{i}"));

        var act = () => Read(header + "\n1");

        act.Should().Throw<ImportFileException>().Which.Problem.Should().Be(ImportFileProblem.NotContacts);
    }

    [Fact]
    public void Rejects_binary_content()
    {
        var act = () => CsvReader.Read([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D], "a.csv");

        act.Should().Throw<ImportFileException>().Which.Problem.Should().Be(ImportFileProblem.NotContacts);
    }

    [Fact]
    public void An_empty_file_has_no_people()
    {
        var act = () => Read("   \r\n\r\n");

        act.Should().Throw<ImportFileException>().Which.Problem.Should().Be(ImportFileProblem.NoPeople);
    }

    [Fact]
    public void Cuts_overlong_cells()
    {
        var table = Read("a,b\n" + new string('x', ImportLimits.MaxCsvCellLength + 500) + ",y");

        table.Records.Single().Cells[0].Length.Should().Be(ImportLimits.MaxCsvCellLength);
        table.Records.Single().Cells[1].Should().Be("y");
    }

    [Fact]
    public void Stops_at_2000_records()
    {
        var text = "a\n" + string.Concat(Enumerable.Range(0, ImportLimits.MaxPeople + 25).Select(i => $"row{i}\n"));

        var table = Read(text);

        table.Records.Should().HaveCount(ImportLimits.MaxPeople);
        table.TotalFound.Should().Be(ImportLimits.MaxPeople + 25);
        table.Truncated.Should().BeTrue();
    }

    [Fact]
    public void Never_throws_on_random_text()
    {
        var random = new Random(11);
        var alphabet = "ab,;\t\"\r\n é";
        for (var round = 0; round < 200; round++)
        {
            var text = new string(Enumerable.Range(0, random.Next(0, 200)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var act = () => Read(text);
            try
            {
                act();
            }
            catch (ImportFileException)
            {
                // A file with no header is refused with a code; that is the only allowed failure.
            }
        }
    }
}

public sealed class CsvMappingPresetsTests
{
    private static CsvTable Table(string text) => CsvReader.Read(Encoding.UTF8.GetBytes(text), "a.csv");

    [Fact]
    public void Recognises_google()
    {
        CsvMappingPresets.Suggest(ImportFixtures.Csv("google-contacts.csv")).Preset.Should().Be(CsvPreset.Google);
    }

    [Fact]
    public void Recognises_google_legacy()
    {
        CsvMappingPresets.Suggest(ImportFixtures.Csv("google-contacts-legacy.csv")).Preset.Should().Be(CsvPreset.GoogleLegacy);
    }

    [Fact]
    public void Recognises_outlook()
    {
        CsvMappingPresets.Suggest(ImportFixtures.Csv("outlook.csv")).Preset.Should().Be(CsvPreset.Outlook);
    }

    [Fact]
    public void Pairs_google_value_columns_with_their_label_columns()
    {
        var table = ImportFixtures.Csv("google-contacts.csv");
        var mapping = CsvMappingPresets.Suggest(table).Mapping;
        int Index(string header) => table.Headers.ToList().IndexOf(header);

        var email1 = mapping.Columns[Index("E-mail 1 - Value")];
        email1.Field.Should().Be(CsvField.Email);
        email1.LabelColumnIndex.Should().Be(Index("E-mail 1 - Label"));
        mapping.Columns[Index("E-mail 1 - Label")].Field.Should().Be(CsvField.Label);
        mapping.Columns[Index("Phone 1 - Value")].LabelColumnIndex.Should().Be(Index("Phone 1 - Label"));
        mapping.Columns[Index("Address 1 - Formatted")].Field.Should().Be(CsvField.Address);
        mapping.Columns[Index("Organization Name")].Field.Should().Be(CsvField.Ignore);
        mapping.Columns[Index("Labels")].Field.Should().Be(CsvField.Ignore);
        mapping.Columns.Should().HaveCount(table.Headers.Count);
    }

    [Fact]
    public void Maps_outlook_phone_columns_to_labels()
    {
        var table = ImportFixtures.Csv("outlook.csv");
        var mapping = CsvMappingPresets.Suggest(table).Mapping;
        CsvColumnAssignment For(string header) => mapping.Columns[table.Headers.ToList().IndexOf(header)];

        For("Mobile Phone").Should().Match<CsvColumnAssignment>(c => c.Field == CsvField.Phone && c.FixedLabel == "Mobile");
        For("Home Phone 2").FixedLabel.Should().Be("Home");
        For("Business Phone").FixedLabel.Should().Be("Work");
        For("Primary Phone").FixedLabel.Should().Be("Main");
        For("Other Phone").FixedLabel.Should().BeNull();
        For("Other Phone").Field.Should().Be(CsvField.Phone);
        For("Home Fax").Field.Should().Be(CsvField.Ignore);
        For("Pager").Field.Should().Be(CsvField.Ignore);
        For("E-mail 2 Address").Field.Should().Be(CsvField.Email);
    }

    [Fact]
    public void Groups_outlook_address_columns_into_blocks()
    {
        var table = ImportFixtures.Csv("outlook.csv");
        var mapping = CsvMappingPresets.Suggest(table).Mapping;

        mapping.AddressBlocks.Select(block => block.Label).Should().Equal("Home", "Work", null);
        var home = mapping.AddressBlocks[0];
        home.ColumnIndexes.Select(index => table.Headers[index]).Should().Equal(
            "Home Street", "Home Street 2", "Home Street 3", "Home City", "Home State", "Home Postal Code", "Home Country/Region");
        home.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Maps_generic_synonyms()
    {
        var table = Table("Given_Name,Surname,Full Name,E-mail,Mobile,tel,DOB,Comments,Nick Name,Postal Address\n");
        var mapping = CsvMappingPresets.Suggest(table).Mapping;

        mapping.Columns.Select(column => column.Field).Should().Equal(
            CsvField.FirstName, CsvField.LastName, CsvField.FullName, CsvField.Email, CsvField.Phone, CsvField.Phone,
            CsvField.Birthday, CsvField.Notes, CsvField.Nickname, CsvField.Address);
        mapping.Columns[4].FixedLabel.Should().Be("Mobile");
        mapping.HasName.Should().BeTrue();
    }

    [Fact]
    public void Leaves_unknown_columns_ignored()
    {
        var mapping = CsvMappingPresets.Suggest(Table("Vorname,Nachname\nAnke,Schmidt")).Mapping;

        mapping.Columns.Should().OnlyContain(column => column.Field == CsvField.Ignore);
        mapping.HasName.Should().BeFalse();
    }

    [Theory]
    [InlineData("Birthday\n15/04/1985", DateOrder.DayMonthYear, false)]
    [InlineData("Birthday\n04/15/1985", DateOrder.MonthDayYear, false)]
    [InlineData("Birthday\n1985-04-15", DateOrder.YearMonthDay, false)]
    [InlineData("Birthday\n--04-15", DateOrder.YearMonthDay, false)]
    [InlineData("Birthday\n03/04/1990\n15.04.1985", DateOrder.DayMonthYear, false)]
    public void Detects_date_order_from_evidence(string csv, DateOrder expected, bool ambiguous)
    {
        var suggestion = CsvMappingPresets.Suggest(Table(csv));

        suggestion.Mapping.DateOrder.Should().Be(expected);
        suggestion.DateOrderIsAmbiguous.Should().Be(ambiguous);
    }

    [Fact]
    public void Marks_date_order_ambiguous_without_evidence()
    {
        var suggestion = CsvMappingPresets.Suggest(Table("Birthday\n03/04/1990\n01/02/1985"));

        suggestion.DateOrderIsAmbiguous.Should().BeTrue();
        suggestion.Mapping.DateOrder.Should().Be(DateOrder.DayMonthYear);
    }

    [Fact]
    public void Mapping_edits_return_new_mappings()
    {
        var mapping = CsvMappingPresets.Suggest(ImportFixtures.Csv("outlook.csv")).Mapping;

        var edited = mapping.WithField(0, CsvField.Ignore).WithAddressBlock(0, false).WithDateOrder(DateOrder.DayMonthYear);

        edited.Columns[0].Field.Should().Be(CsvField.Ignore);
        edited.AddressBlocks[0].Enabled.Should().BeFalse();
        edited.DateOrder.Should().Be(DateOrder.DayMonthYear);
        mapping.Columns[0].Field.Should().Be(CsvField.FirstName);
        mapping.AddressBlocks[0].Enabled.Should().BeTrue();
    }
}

public sealed class CsvDraftMapperTests
{
    private static ImportReadResult Map(string csv, Func<CsvColumnMapping, CsvColumnMapping>? edit = null)
    {
        var table = CsvReader.Read(Encoding.UTF8.GetBytes(csv), "a.csv");
        var mapping = CsvMappingPresets.Suggest(table).Mapping;
        return CsvDraftMapper.Map(table, edit is null ? mapping : edit(mapping));
    }

    [Fact]
    public void Splits_google_multi_values()
    {
        var read = Map("First Name,E-mail 1 - Label,E-mail 1 - Value\nAnn,* Home ::: Work,a@example.com ::: b@example.org\n");

        read.People.Single().ContactMethods.Should().Equal(
            new ImportContactDraft(ContactMethodKind.Email, "Home", "a@example.com"),
            new ImportContactDraft(ContactMethodKind.Email, "Work", "b@example.org"));
    }

    [Fact]
    public void Strips_google_star_labels()
    {
        var read = Map("First Name,Phone 1 - Label,Phone 1 - Value,Phone 2 - Label,Phone 2 - Value,Phone 3 - Label,Phone 3 - Value\nAnn,* Mobile,+44 7700 900001,Other,+44 7700 900002,Work Fax,+44 20 7946 0003\n");

        read.People.Single().ContactMethods.Should().Equal(
            new ImportContactDraft(ContactMethodKind.Phone, "Mobile", "+44 7700 900001"),
            new ImportContactDraft(ContactMethodKind.Phone, null, "+44 7700 900002"));
    }

    [Fact]
    public void Uses_full_name_only_without_first_and_last()
    {
        var read = Map("Name,First\nMary Jane Watson,\nIgnored Name,Zed\n");

        read.People[0].FirstName.Should().Be("Mary Jane");
        read.People[0].LastName.Should().Be("Watson");
        read.People[1].FirstName.Should().Be("Zed");
        read.People[1].LastName.Should().BeNull();
    }

    [Fact]
    public void Joins_two_first_name_columns()
    {
        var read = Map("First,Name2,Last\nAnn,Marie,Ex\n", mapping => mapping.WithField(1, CsvField.FirstName));

        read.People.Single().FirstName.Should().Be("Ann Marie");
    }

    [Fact]
    public void Adds_middle_names_after_the_first_name()
    {
        var read = Map("First Name,Middle Name,Last Name,E-mail 1 - Value\nAnn,Marie,Ex,a@example.com\n");

        read.People.Single().FirstName.Should().Be("Ann Marie");
    }

    [Fact]
    public void Builds_address_from_blocks()
    {
        var read = ImportFixtures.MappedCsv("outlook.csv").Read;

        read.Person("René").Contact(ContactMethodKind.Address).Value.Should().Be("Hauptstraße 5\nKöln\n50667\nGermany");
    }

    [Fact]
    public void A_disabled_address_block_is_not_imported()
    {
        var table = ImportFixtures.Csv("outlook.csv");
        var mapping = CsvMappingPresets.Suggest(table).Mapping.WithAddressBlock(0, false);

        var read = CsvDraftMapper.Map(table, mapping);

        read.Person("René").ContactMethods.Should().NotContain(contact => contact.Kind == ContactMethodKind.Address);
    }

    [Fact]
    public void Counts_empty_records_as_skipped()
    {
        var read = Map("First,Other\nAnn,x\n,only-ignored\n");

        read.People.Should().ContainSingle();
        read.SkippedEmpty.Should().Be(1);
        read.TotalFound.Should().Be(1);
    }

    [Fact]
    public void Ignores_unmapped_columns()
    {
        var read = Map("First,Secret\nAnn,top secret\n");

        read.People.Single().Details.Should().BeNull();
        read.People.Single().ContactMethods.Should().BeEmpty();
    }

    [Fact]
    public void Keeps_note_newlines_and_marks_unreadable_birthdays()
    {
        var read = Map("First,Birthday,Notes\nAnn,someday,\"a\r\nb\"\n");

        var person = read.People.Single();
        person.Details.Should().Be("a\nb");
        person.BirthdayUnreadable.Should().BeTrue();
        person.Birthday.Should().BeNull();
    }

    [Fact]
    public void Carries_a_truncated_table_through()
    {
        var csv = "First\n" + string.Concat(Enumerable.Range(0, ImportLimits.MaxPeople + 10).Select(i => $"P{i}\n"));

        var read = Map(csv);

        read.People.Should().HaveCount(ImportLimits.MaxPeople);
        read.Truncated.Should().BeTrue();
        read.TotalFound.Should().Be(ImportLimits.MaxPeople + 10);
    }
}
