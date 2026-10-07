using Relio.Application.People.Import;
using Relio.Domain;

namespace Relio.Application.Tests.People.Import;

/// <summary>One test per fixture file: the counts and the key fields, read end to end.</summary>
public sealed class ImportFixtureTests
{
    [Fact]
    public void Ios_17_vcard_gives_three_people()
    {
        var read = ImportFixtures.VCard("ios-17.vcf");

        read.People.Should().HaveCount(3);
        read.SkippedEmpty.Should().Be(0);
        read.Truncated.Should().BeFalse();

        var marta = read.Person("Marta");
        marta.LastName.Should().Be("Bianchi");
        marta.Birthday.Should().Be(new ImportBirthdayDraft(12, 10, 1985));
        marta.Details.Should().Be("Met at the book fair.\nLoves tea, not coffee; ask about the garden\\ shed.");
        marta.ContactMethods.Select(contact => contact.Kind).Should().Equal(
            ContactMethodKind.Email, ContactMethodKind.Phone, ContactMethodKind.Address, ContactMethodKind.Social);
        marta.Contact(ContactMethodKind.Email).Should().Be(new ImportContactDraft(ContactMethodKind.Email, "Home", "marta@example.com"));
        marta.Contact(ContactMethodKind.Phone).Should().Be(new ImportContactDraft(ContactMethodKind.Phone, "Mobile", "+44 7700 900123"));
        marta.Contact(ContactMethodKind.Address).Value.Should().Be("12 Example Square\nLondon\nN1 1AA\nUnited Kingdom");
        marta.Contact(ContactMethodKind.Social).Should().Be(new ImportContactDraft(ContactMethodKind.Social, "Twitter", "marta_b"));

        var ada = read.Person("Ada");
        ada.Birthday.Should().Be(new ImportBirthdayDraft(4, 15, null));
        ada.BirthdayUnreadable.Should().BeFalse();
        ada.Contact(ContactMethodKind.Phone).Should().Be(new ImportContactDraft(ContactMethodKind.Phone, "Studio", "+1 202 555 0142"));

        var beatrice = read.Person("Beatrice");
        beatrice.Nickname.Should().Be("Bea");
        beatrice.Contact(ContactMethodKind.Email).Should().Be(new ImportContactDraft(ContactMethodKind.Email, null, "bea@example.net"));
        beatrice.RowNumber.Should().Be(3);
    }

    [Fact]
    public void Android_21_quoted_printable_vcard_gives_four_people()
    {
        var read = ImportFixtures.VCard("android-21-qp.vcf");

        read.People.Should().HaveCount(4);

        var jurgen = read.Person("Jürgen");
        jurgen.LastName.Should().Be("Müller");
        jurgen.Birthday.Should().Be(new ImportBirthdayDraft(3, 1, 1990));
        jurgen.Details.Should().Be("Zweite Zeile ähnlich\nund eine lange Notiz, die mit einem weichen Umbruch weitergeht.");
        jurgen.ContactMethods.Should().Equal(
            new ImportContactDraft(ContactMethodKind.Phone, "Mobile", "+44 7700 900456"),
            new ImportContactDraft(ContactMethodKind.Phone, "Home", "020 7946 0018"),
            new ImportContactDraft(ContactMethodKind.Email, "Home", "jurgen@example.org"));

        var ana = read.Person("Ana");
        ana.LastName.Should().Be("René");
        ana.Birthday.Should().Be(new ImportBirthdayDraft(4, 15, null));

        var nameless = read.People.Single(person => person.FirstName is null);
        nameless.ContactMethods.Should().ContainSingle().Which.Value.Should().Be("+44 7700 900789");

        var fiona = read.Person("Fiona");
        fiona.ContactMethods.Should().ContainSingle().Which.Value.Should().Be("+44 7700 900321");
        read.People.Should().NotContain(person => person.FirstName == "Card");
    }

    [Fact]
    public void Google_30_vcard_rejoins_characters_split_across_folds()
    {
        var read = ImportFixtures.VCard("google-30.vcf");

        read.People.Should().HaveCount(3);

        var zoe = read.Person("Zoë");
        zoe.LastName.Should().Be("Example");
        zoe.Birthday.Should().Be(new ImportBirthdayDraft(2, 28, 1979));
        zoe.Contact(ContactMethodKind.Email).Label.Should().Be("Work");
        zoe.Contact(ContactMethodKind.Address).Value.Should().Be("100 Sample Street\nApt 4\nSpringfield\n12345\nUSA");
        zoe.ContactMethods.Should().HaveCount(3);

        read.Person("Ngozi").Birthday.Should().Be(new ImportBirthdayDraft(2, 29, null));

        var asa = read.Person("Asa");
        asa.Details.Should().Be(new string('a', 69) + "é" + new string('b', 72) + "漢" + " and the end of the note.");
    }

    [Fact]
    public void Vcard_40_reads_structured_names_uris_and_unreadable_birthdays()
    {
        var read = ImportFixtures.VCard("vcard-40.vcf");

        read.People.Should().HaveCount(2);

        var zoe = read.Person("Zoë Anne");
        zoe.LastName.Should().Be("Example");
        zoe.Birthday.Should().Be(new ImportBirthdayDraft(4, 15, null));
        zoe.Contact(ContactMethodKind.Phone).Should().Be(new ImportContactDraft(ContactMethodKind.Phone, "Mobile", "+1-202-555-0143"));
        zoe.Contact(ContactMethodKind.Email).Label.Should().Be("Work");

        var sam = read.Person("Sam");
        sam.Birthday.Should().BeNull();
        sam.BirthdayUnreadable.Should().BeTrue();
    }

    [Fact]
    public void Edge_cases_vcard_reads_what_it_can_and_skips_the_rest()
    {
        var read = ImportFixtures.VCard("edge-cases.vcf");

        read.People.Should().HaveCount(8);
        read.People.Select(person => person.RowNumber).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);

        read.Person("Lena").LastName.Should().Be("Lowercase");
        read.Person("Emails").ContactMethods.Should().HaveCount(25);
        read.People.Single(person => person.FirstName!.Length == 101).LastName.Should().Be("Surname");
        read.Person("Notes").Details!.Length.Should().Be(5000);

        var mail = read.Person("Mail");
        mail.Contact(ContactMethodKind.Email).Value.Should().Be("not-an-email");
        mail.Contact(ContactMethodKind.Phone).Value.Should().Be("+44 7700 900111 ext 5");

        var mary = read.Person("Mary Jane");
        mary.LastName.Should().Be("Watson");

        read.Person("Folded").LastName.Should().Be("Tabby");
        read.Person("Final").ContactMethods.Should().ContainSingle();
    }

    [Fact]
    public void Google_contacts_csv_is_recognised_and_mapped()
    {
        var (suggestion, read) = ImportFixtures.MappedCsv("google-contacts.csv");

        suggestion.Preset.Should().Be(CsvPreset.Google);
        suggestion.Mapping.HasName.Should().BeTrue();
        read.People.Should().HaveCount(3);
        read.SkippedEmpty.Should().Be(1);

        var marta = read.Person("Marta");
        marta.RowNumber.Should().Be(2);
        marta.LastName.Should().Be("Bianchi");
        marta.Nickname.Should().Be("Marti");
        marta.Birthday.Should().Be(new ImportBirthdayDraft(4, 15, 1985));
        marta.Details.Should().Be("Line one\nLine \"two\"");
        marta.ContactMethods.Should().Equal(
            new ImportContactDraft(ContactMethodKind.Email, "Home", "marta@example.com"),
            new ImportContactDraft(ContactMethodKind.Email, "Work", "marta.b@example.org"),
            new ImportContactDraft(ContactMethodKind.Phone, "Mobile", "+44 7700 900123"),
            new ImportContactDraft(ContactMethodKind.Address, "Home", "12 Example Square\nLondon"));

        var jurgen = read.Person("Jürgen Karl");
        jurgen.LastName.Should().Be("Müller");
        jurgen.Birthday.Should().Be(new ImportBirthdayDraft(4, 15, null));

        read.People.Single(person => person.FirstName is null).Contact(ContactMethodKind.Email).Label.Should().Be("Home");
    }

    [Fact]
    public void Google_contacts_legacy_csv_is_read_as_utf16()
    {
        var table = ImportFixtures.Csv("google-contacts-legacy.csv");
        table.Encoding.Should().Be(ImportTextEncoding.Utf16LittleEndian);

        var (suggestion, read) = ImportFixtures.MappedCsv("google-contacts-legacy.csv");

        suggestion.Preset.Should().Be(CsvPreset.GoogleLegacy);
        read.People.Should().HaveCount(2);
        var marta = read.Person("Marta");
        marta.Birthday.Should().Be(new ImportBirthdayDraft(4, 15, 1985));
        marta.Contact(ContactMethodKind.Email).Label.Should().Be("Home");
        marta.Contact(ContactMethodKind.Phone).Label.Should().Be("Mobile");
        read.Person("Zoë").Contact(ContactMethodKind.Email).Label.Should().Be("Work");
    }

    [Fact]
    public void Outlook_csv_is_read_as_windows_1252_with_month_first_dates()
    {
        var table = ImportFixtures.Csv("outlook.csv");
        table.Encoding.Should().Be(ImportTextEncoding.Windows1252);
        table.Headers.Count.Should().BeGreaterThan(80);

        var (suggestion, read) = ImportFixtures.MappedCsv("outlook.csv");

        suggestion.Preset.Should().Be(CsvPreset.Outlook);
        suggestion.Mapping.DateOrder.Should().Be(DateOrder.MonthDayYear);
        suggestion.DateOrderIsAmbiguous.Should().BeFalse();
        read.People.Should().HaveCount(3);

        var rene = read.Person("René");
        rene.LastName.Should().Be("Dupont");
        rene.Birthday.Should().Be(new ImportBirthdayDraft(4, 15, 1985));
        rene.Details.Should().Be("Straße note");
        rene.ContactMethods.Should().Contain(new ImportContactDraft(ContactMethodKind.Phone, "Mobile", "+44 7700 900123"));
        rene.ContactMethods.Should().Contain(new ImportContactDraft(ContactMethodKind.Phone, "Home", "020 7946 0123"));
        rene.ContactMethods.Should().Contain(new ImportContactDraft(ContactMethodKind.Phone, "Work", "+44 20 7946 0124"));
        rene.ContactMethods.Should().Contain(new ImportContactDraft(ContactMethodKind.Address, "Home", "Hauptstraße 5\nKöln\n50667\nGermany"));

        var jorg = read.Person("Jörg");
        jorg.LastName.Should().Be("Weiß");
        jorg.Birthday.Should().BeNull();
        jorg.BirthdayUnreadable.Should().BeFalse();
        jorg.Contact(ContactMethodKind.Phone).Label.Should().BeNull();

        var zoe = read.Person("Zoé");
        zoe.Birthday.Should().Be(new ImportBirthdayDraft(12, 31, 1990));
        zoe.Contact(ContactMethodKind.Address).Should().Be(new ImportContactDraft(ContactMethodKind.Address, "Work", "Mühlenweg 1\nMünchen\n80331"));
    }

    [Fact]
    public void Semicolon_csv_with_german_headers_needs_manual_mapping()
    {
        var table = ImportFixtures.Csv("semicolon-de.csv");
        table.Delimiter.Should().Be(';');
        table.Encoding.Should().Be(ImportTextEncoding.Utf8);

        var suggestion = CsvMappingPresets.Suggest(table);
        suggestion.Preset.Should().Be(CsvPreset.Generic);
        suggestion.Mapping.HasName.Should().BeFalse();

        var mapping = suggestion.Mapping
            .WithField(0, CsvField.FirstName)
            .WithField(1, CsvField.LastName)
            .WithField(2, CsvField.Email)
            .WithField(3, CsvField.Phone)
            .WithField(4, CsvField.Birthday)
            .WithDateOrder(DateOrder.DayMonthYear);
        var read = CsvDraftMapper.Map(table, mapping);

        read.People.Should().HaveCount(2);
        read.Person("Jürgen").Birthday.Should().Be(new ImportBirthdayDraft(4, 15, 1985));
        read.Person("Anke").Birthday.Should().Be(new ImportBirthdayDraft(2, 3, 1990));
        CsvMappingPresets.DetectDateOrder(table, [4]).Decisive.Should().Be(DateOrder.DayMonthYear);
    }

    [Fact]
    public void Generic_tsv_handles_ragged_rows_blank_lines_and_quoted_tabs()
    {
        var table = ImportFixtures.Csv("generic.tsv");
        table.Delimiter.Should().Be('\t');
        table.Records.Should().HaveCount(4);
        table.Records.Select(record => record.RecordNumber).Should().Equal(2, 3, 5, 6);

        var (suggestion, read) = ImportFixtures.MappedCsv("generic.tsv");
        suggestion.Preset.Should().Be(CsvPreset.Generic);
        suggestion.Mapping.DateOrder.Should().Be(DateOrder.MonthDayYear);

        read.People.Should().HaveCount(4);
        var ada = read.Person("Ada");
        ada.LastName.Should().Be("Lovelace");
        ada.Birthday.Should().Be(new ImportBirthdayDraft(12, 10, 1815));

        var tab = read.People.Single(person => person.RowNumber == 5);
        tab.FirstName.Should().Be("Tab");
        tab.LastName.Should().Be("Person");
        tab.BirthdayUnreadable.Should().BeTrue();
        tab.Details.Should().Be("Notes with\ttab");
    }
}
