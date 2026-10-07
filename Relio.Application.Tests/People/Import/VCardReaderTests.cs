using System.Text;
using Relio.Application.People.Import;
using Relio.Domain;

namespace Relio.Application.Tests.People.Import;

public sealed class VCardReaderTests
{
    private static ImportReadResult Read(string text) => VCardReader.Read(Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n", StringComparison.Ordinal)));

    private static ImportPersonDraft One(string body, string version = "3.0")
    {
        var read = Read($"BEGIN:VCARD\nVERSION:{version}\n{body}\nEND:VCARD\n");
        return read.People.Should().ContainSingle().Subject;
    }

    [Fact]
    public void Unfolds_lines_folded_with_a_space_or_a_tab()
    {
        var person = One("N:Bi\n anchi;Ma\n\trta;;;");

        person.FirstName.Should().Be("Marta");
        person.LastName.Should().Be("Bianchi");
    }

    [Fact]
    public void Unfolds_before_decoding_so_a_split_utf8_character_survives()
    {
        var bytes = new List<byte>(Encoding.UTF8.GetBytes("BEGIN:VCARD\r\nVERSION:3.0\r\nN:Ex;Zo"));
        bytes.Add(0xC3);
        bytes.AddRange("\r\n "u8);
        bytes.Add(0xAB);
        bytes.AddRange(Encoding.UTF8.GetBytes(";;;\r\nEND:VCARD\r\n"));

        VCardReader.Read(bytes.ToArray()).People.Single().FirstName.Should().Be("Zoë");
    }

    [Fact]
    public void Decodes_quoted_printable_with_soft_line_breaks()
    {
        var person = One("N:Ex;Ann;;;\nNOTE;ENCODING=QUOTED-PRINTABLE:one =\ntwo=0Athree", "2.1");

        person.Details.Should().Be("one two\nthree");
    }

    [Fact]
    public void Honours_charset_utf8_and_iso_8859_1()
    {
        One("N;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:M=C3=BCller;Jo;;;", "2.1").LastName.Should().Be("Müller");
        One("N;CHARSET=ISO-8859-1;ENCODING=QUOTED-PRINTABLE:Ren=E9;Jo;;;", "2.1").LastName.Should().Be("René");

        // Without a charset, bytes that are not UTF-8 are read as Windows-1252.
        One("N;ENCODING=QUOTED-PRINTABLE:Ren=E9;Jo;;;", "2.1").LastName.Should().Be("René");
    }

    [Fact]
    public void Unescapes_commas_semicolons_backslashes_and_newlines_in_v3()
    {
        One("N:Ex;Ann;;;\nNOTE:a\\, b\\; c \\\\ d\\ne\\Nf").Details.Should().Be("a, b; c \\ d\ne\nf");
    }

    [Fact]
    public void Unescapes_only_semicolons_in_v21()
    {
        One("N:Ex;Ann;;;\nNOTE:a\\nb\\;c", "2.1").Details.Should().Be("a\\nb;c");
    }

    [Fact]
    public void Reads_v21_bare_type_parameters()
    {
        var person = One("N:Ex;Ann;;;\nTEL;CELL;VOICE:+44 7700 900001\nTEL;HOME:+44 7700 900002\nTEL;WORK;PREF:+44 7700 900003", "2.1");

        person.ContactMethods.Select(contact => contact.Label).Should().Equal("Mobile", "Home", "Work");
    }

    [Fact]
    public void Reads_quoted_and_comma_listed_type_parameters()
    {
        var person = One("N:Ex;Ann;;;\nTEL;TYPE=\"cell,voice\":+44 7700 900001\nEMAIL;TYPE=INTERNET,WORK:a@example.com");

        person.Contact(ContactMethodKind.Phone).Label.Should().Be("Mobile");
        person.Contact(ContactMethodKind.Email).Label.Should().Be("Work");
    }

    [Fact]
    public void Maps_n_to_first_and_last_name_with_additional_names()
    {
        var person = One("N:Watson;Mary;Jane;Dr.;PhD");

        person.FirstName.Should().Be("Mary Jane");
        person.LastName.Should().Be("Watson");
    }

    [Fact]
    public void Uses_family_name_as_first_name_when_given_is_missing()
    {
        var person = One("N:Cher;;;;");

        person.FirstName.Should().Be("Cher");
        person.LastName.Should().BeNull();
    }

    [Fact]
    public void Splits_fn_at_the_last_space_when_there_is_no_n()
    {
        var person = One("FN:Mary Jane Watson");
        (person.FirstName, person.LastName).Should().Be(("Mary Jane", "Watson"));

        var single = One("FN:Cher");
        (single.FirstName, single.LastName).Should().Be(("Cher", null));
    }

    [Fact]
    public void Prefers_n_over_fn()
    {
        var person = One("N:Bianchi;Marta;;;\nFN:Somebody Else");

        (person.FirstName, person.LastName).Should().Be(("Marta", "Bianchi"));
    }

    [Fact]
    public void A_card_with_only_an_org_has_no_name()
    {
        var read = Read("BEGIN:VCARD\nVERSION:3.0\nORG:Example Ltd\nEND:VCARD\n");

        read.People.Should().BeEmpty();
        read.SkippedEmpty.Should().Be(1);

        var withEmail = One("ORG:Example Ltd\nEMAIL:a@example.com");
        withEmail.FirstName.Should().BeNull();
    }

    [Theory]
    [InlineData("BDAY:1985-12-10", 12, 10, 1985)]
    [InlineData("BDAY:19851210", 12, 10, 1985)]
    [InlineData("BDAY:1985-12-10T08:30:00Z", 12, 10, 1985)]
    [InlineData("BDAY:--0415", 4, 15, null)]
    [InlineData("BDAY:--04-15", 4, 15, null)]
    [InlineData("BDAY:--0229", 2, 29, null)]
    [InlineData("BDAY;X-APPLE-OMIT-YEAR=1604:1604-04-15", 4, 15, null)]
    [InlineData("BDAY;X-APPLE-OMIT-YEAR=1900:1900-04-15", 4, 15, null)]
    public void Reads_birthdays(string line, int month, int day, int? year)
    {
        One($"N:Ex;Ann;;;\n{line}").Birthday.Should().Be(new ImportBirthdayDraft(month, day, year));
    }

    [Theory]
    [InlineData("BDAY;VALUE=text:circa 1800")]
    [InlineData("BDAY:not a date")]
    [InlineData("BDAY:1985-13-40")]
    [InlineData("BDAY:19850230")]
    public void Flags_an_unreadable_bday(string line)
    {
        var person = One($"N:Ex;Ann;;;\n{line}");

        person.Birthday.Should().BeNull();
        person.BirthdayUnreadable.Should().BeTrue();
    }

    [Fact]
    public void An_empty_bday_is_no_birthday_and_not_a_problem()
    {
        var person = One("N:Ex;Ann;;;\nBDAY:");

        person.Birthday.Should().BeNull();
        person.BirthdayUnreadable.Should().BeFalse();
    }

    [Fact]
    public void Prefers_the_group_x_ablabel_and_unwraps_apple_labels()
    {
        var person = One("N:Ex;Ann;;;\nitem1.TEL:+44 7700 900001\nitem1.X-ABLabel:_$!<Mobile>!$_\nitem2.TEL;type=CELL:+44 7700 900002\nitem2.X-ABLabel:Studio\nitem3.TEL;type=WORK:+44 7700 900003\nitem3.X-ABLabel:_$!<Other>!$_");

        person.ContactMethods.Select(contact => contact.Label).Should().Equal("Mobile", "Studio", null);
    }

    [Fact]
    public void Strips_tel_uri_prefix_and_extension()
    {
        One("N:Ex;Ann;;;\nTEL;VALUE=uri:tel:+1-202-555-0143;ext=12", "4.0").Contact(ContactMethodKind.Phone).Value
            .Should().Be("+1-202-555-0143");
    }

    [Fact]
    public void Skips_fax_numbers()
    {
        One("N:Ex;Ann;;;\nTEL;TYPE=FAX:+44 20 7946 0001\nTEL;TYPE=work,fax:+44 20 7946 0002\nTEL;TYPE=WORK:+44 20 7946 0003")
            .ContactMethods.Should().ContainSingle().Which.Value.Should().Be("+44 20 7946 0003");
    }

    [Fact]
    public void Joins_adr_parts_with_newlines()
    {
        One("N:Ex;Ann;;;\nADR;TYPE=HOME:PO Box 1;Suite 2;1 Example Road;Springfield;ST;12345;USA").Contact(ContactMethodKind.Address)
            .Should().Be(new ImportContactDraft(ContactMethodKind.Address, "Home", "PO Box 1\nSuite 2\n1 Example Road\nSpringfield\nST\n12345\nUSA"));
    }

    [Fact]
    public void Prefers_the_adr_label_parameter()
    {
        One("N:Ex;Ann;;;\nADR;LABEL=\"1 Example Road\\nSpringfield, ST\":;;1 Example Road;Springfield;ST;;").Contact(ContactMethodKind.Address)
            .Value.Should().Be("1 Example Road\nSpringfield, ST");
    }

    [Fact]
    public void Reads_x_socialprofile_with_x_user()
    {
        var person = One("N:Ex;Ann;;;\nX-SOCIALPROFILE;type=twitter;x-user=ann_ex:https://x.example.com/ann_ex\nX-SOCIALPROFILE;type=facebook:https://facebook.example.com/ann");

        person.ContactMethods.Should().Equal(
            new ImportContactDraft(ContactMethodKind.Social, "Twitter", "ann_ex"),
            new ImportContactDraft(ContactMethodKind.Social, "Facebook", "https://facebook.example.com/ann"));
    }

    [Fact]
    public void Keeps_note_newlines()
    {
        One("N:Ex;Ann;;;\nNOTE:one\\ntwo\\n\\nfour").Details.Should().Be("one\ntwo\n\nfour");
    }

    [Fact]
    public void Ignores_everything_it_does_not_use()
    {
        var person = One("N:Ex;Ann;;;\nORG:Example Ltd\nTITLE:Boss\nURL:https://example.com\nCATEGORIES:a,b\nIMPP:xmpp:ann@example.com\nX-CUSTOM:1\nREV:20240101T000000Z");

        person.ContactMethods.Should().BeEmpty();
        person.Details.Should().BeNull();
    }

    [Fact]
    public void Skips_photo_and_overlong_lines()
    {
        var huge = new string('A', ImportLimits.MaxVCardLineLength + 10);
        var person = One($"N:Ex;Ann;;;\nPHOTO;ENCODING=b;TYPE=JPEG:{huge}\nNOTE:{huge}\nEMAIL:ann@example.com");

        person.Details.Should().BeNull();
        person.ContactMethods.Should().ContainSingle();
    }

    [Fact]
    public void Ignores_base64_encoded_values_of_properties_it_uses()
    {
        One("N:Ex;Ann;;;\nNOTE;ENCODING=BASE64:SGVsbG8=").Details.Should().BeNull();
    }

    [Fact]
    public void Skips_nested_agent_cards()
    {
        var read = Read("BEGIN:VCARD\nVERSION:2.1\nN:Outer;Ann;;;\nAGENT:\nBEGIN:VCARD\nVERSION:2.1\nN:Inner;Bob;;;\nTEL:+44 7700 900001\nEND:VCARD\nEMAIL:ann@example.com\nEND:VCARD\n");

        var person = read.People.Should().ContainSingle().Subject;
        person.FirstName.Should().Be("Ann");
        person.ContactMethods.Should().ContainSingle().Which.Kind.Should().Be(ContactMethodKind.Email);
    }

    [Fact]
    public void Reads_many_cards_and_ignores_text_between_them()
    {
        var read = Read("hello\nBEGIN:VCARD\nN:A;Ann;;;\nEND:VCARD\nnoise\n\nBEGIN:VCALENDAR\nEND:VCALENDAR\nBEGIN:VCARD\nN:B;Bob;;;\nEND:VCARD\n");

        read.People.Select(person => person.FirstName).Should().Equal("Ann", "Bob");
        read.People.Select(person => person.RowNumber).Should().Equal(1, 2);
    }

    [Fact]
    public void Keeps_a_final_card_without_end()
    {
        Read("BEGIN:VCARD\nN:A;Ann;;;\nEND:VCARD\nBEGIN:VCARD\nN:B;Bob;;;\n").People.Should().HaveCount(2);
        Read("BEGIN:VCARD\n").People.Should().BeEmpty();
    }

    [Fact]
    public void Is_case_insensitive_for_begin_end_and_property_names()
    {
        var read = Read("begin:vcard\nn:A;Ann;;;\nemail;type=work:a@example.com\nend:vcard\n");

        read.People.Should().ContainSingle().Which.ContactMethods.Should().ContainSingle().Which.Label.Should().Be("Work");
    }

    [Fact]
    public void Drops_repeated_contact_values_within_a_card()
    {
        var person = One("N:Ex;Ann;;;\nEMAIL:Ann@Example.com\nEMAIL:ann@example.com\nTEL:+44 7700 900001\nTEL:+44-7700-900001");

        person.ContactMethods.Should().HaveCount(2);
        person.Contact(ContactMethodKind.Email).Value.Should().Be("Ann@Example.com");
    }

    [Fact]
    public void Stops_at_2000_people_and_reports_truncation()
    {
        var text = string.Concat(Enumerable.Range(0, ImportLimits.MaxPeople + 50).Select(index => $"BEGIN:VCARD\nN:P;Person{index};;;\nEND:VCARD\n"));

        var read = Read(text);

        read.People.Should().HaveCount(ImportLimits.MaxPeople);
        read.TotalFound.Should().Be(ImportLimits.MaxPeople + 50);
        read.Truncated.Should().BeTrue();
    }

    [Fact]
    public void Reads_utf16_files()
    {
        var text = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Ex;Zoë;;;\r\nEND:VCARD\r\n";

        VCardReader.Read([0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)]).People.Single().FirstName.Should().Be("Zoë");
        VCardReader.Read(Encoding.Unicode.GetBytes(text)).People.Single().FirstName.Should().Be("Zoë");
    }

    [Fact]
    public void Malformed_lines_are_ignored_not_thrown()
    {
        var random = new Random(7);
        for (var round = 0; round < 100; round++)
        {
            var bytes = new byte[random.Next(0, 400)];
            random.NextBytes(bytes);
            var act = () => VCardReader.Read(bytes);
            act.Should().NotThrow();
        }

        var read = Read("BEGIN:VCARD\n::::\n;;;;\n=\nN\nTEL;;;:\nN:Ex;Ann;;;\nEND:VCARD\n");
        read.People.Single().FirstName.Should().Be("Ann");
    }

    [Fact]
    public void Content_after_a_quoted_printable_soft_break_at_the_end_of_the_file_is_kept()
    {
        var read = Read("BEGIN:VCARD\nVERSION:2.1\nN:Ex;Ann;;;\nNOTE;ENCODING=QUOTED-PRINTABLE:abc=\n");

        read.People.Single().Details.Should().Be("abc");
    }
}
