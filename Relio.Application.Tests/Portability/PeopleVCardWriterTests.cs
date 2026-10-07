using System.Text;
using Relio.Application.People.Import;
using Relio.Application.Portability;
using Relio.Domain;

namespace Relio.Application.Tests.Portability;

public sealed class PeopleVCardWriterTests
{
    [Fact]
    public void Write_round_trips_contact_fields_without_exporting_private_narratives()
    {
        var source = new VCardPerson
        {
            FirstName = "Zoë",
            LastName = "O'Neil",
            Nickname = "Z,",
            BirthdayDay = 29,
            BirthdayMonth = 2,
            BirthdayYear = null,
            ContactMethods =
            [
                new VCardContactMethod(ContactMethodKind.Email, "Work", "zoe@example.com", 0),
                new VCardContactMethod(ContactMethodKind.Phone, "Mobile", "+1 555 123 4567", 1),
                new VCardContactMethod(ContactMethodKind.Address, "Home", "12 Main St\nApt 4", 2),
                new VCardContactMethod(ContactMethodKind.Social, "Mastodon", "@zoe", 3),
                new VCardContactMethod(ContactMethodKind.Other, "Private", "not-for-vcard", 4),
            ],
        };

        var vcard = PeopleVCardWriter.Write([source]);
        var imported = VCardReader.Read(Encoding.UTF8.GetBytes(vcard)).People.Should().ContainSingle().Subject;

        imported.FirstName.Should().Be("Zoë");
        imported.LastName.Should().Be("O'Neil");
        imported.Nickname.Should().Be("Z,");
        imported.Birthday.Should().Be(new ImportBirthdayDraft(2, 29, null));
        imported.ContactMethods.Select(contact => (contact.Kind, contact.Label, contact.Value))
            .Should().Equal(
                (ContactMethodKind.Email, "Work", "zoe@example.com"),
                (ContactMethodKind.Phone, "Mobile", "+1 555 123 4567"),
                (ContactMethodKind.Address, "Home", "12 Main St\nApt 4"),
                (ContactMethodKind.Social, "Mastodon", "@zoe"));
        imported.Details.Should().BeNull();
        vcard.Should().NotContain("not-for-vcard");
        vcard.Should().NotContain("private narrative");
    }

    [Fact]
    public void Write_folds_at_utf8_octet_boundaries_without_splitting_characters()
    {
        var person = new VCardPerson
        {
            FirstName = string.Concat(Enumerable.Repeat("🧭", 30)),
            LastName = null,
            Nickname = null,
            BirthdayDay = null,
            BirthdayMonth = null,
            BirthdayYear = null,
            ContactMethods = [],
        };

        var vcard = PeopleVCardWriter.Write([person]);
        var logicalLines = vcard.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        logicalLines.All(line => Encoding.UTF8.GetByteCount(line) <= 75).Should().BeTrue();
        VCardReader.Read(Encoding.UTF8.GetBytes(vcard)).People.Single().FirstName.Should().Be(person.FirstName);
    }
}
