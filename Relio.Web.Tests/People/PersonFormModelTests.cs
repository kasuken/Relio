using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PersonFormModelTests
{
    [Fact]
    public void FromPerson_copies_the_profile_the_contact_methods_in_order_and_the_tags_by_name()
    {
        var type = new RelationshipType { Name = "Friend" };
        var first = new ContactMethod { Kind = ContactMethodKind.Email, Label = "Work", Value = "ada@example.com", SortOrder = 0 };
        var second = new ContactMethod { Kind = ContactMethodKind.Phone, Value = "+44 7700 900123", SortOrder = 1 };
        var chess = new Tag { Name = "chess" };
        var work = new Tag { Name = "Work" };
        var person = new Person
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Nickname = "Countess",
            RelationshipTypeId = type.Id,
            BirthdayDay = 10,
            BirthdayMonth = 12,
            BirthdayYear = 1815,
            BirthdayReminderDisabled = true,
            BirthdayReminderLeadDays = 7,
            StayInTouchCadenceDays = 30,
            HowWeMet = "At a talk.",
            Details = "Writes letters.",
            ContactMethods = { second, first },
            Tags = { work, chess },
        };

        var model = PersonFormModel.FromPerson(person);

        model.FirstName.Should().Be("Ada");
        model.LastName.Should().Be("Lovelace");
        model.Nickname.Should().Be("Countess");
        model.RelationshipTypeId.Should().Be(type.Id);
        (model.BirthdayDay, model.BirthdayMonth, model.BirthdayYear).Should().Be((10, 12, 1815));
        model.BirthdayReminderDisabled.Should().BeTrue();
        model.BirthdayReminderLeadDays.Should().Be(7);
        model.StayInTouchCadenceDays.Should().Be(30);
        model.HowWeMet.Should().Be("At a talk.");
        model.Details.Should().Be("Writes letters.");
        model.ContactMethods.Select(r => (r.Id, r.Kind, r.Label, r.Value)).Should().Equal(
            (first.Id, ContactMethodKind.Email, "Work", "ada@example.com"),
            (second.Id, ContactMethodKind.Phone, null, "+44 7700 900123"));
        model.Tags.Should().Equal(new TagSelection(chess.Id, "chess"), new TagSelection(work.Id, "Work"));
    }

    [Fact]
    public void Every_row_gets_its_own_key()
    {
        var person = new Person
        {
            FirstName = "Ada",
            ContactMethods =
            {
                new ContactMethod { Value = "a@example.com", SortOrder = 0 },
                new ContactMethod { Value = "b@example.com", SortOrder = 1 },
            },
        };

        PersonFormModel.FromPerson(person).ContactMethods.Select(r => r.Key).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void ToUpdateRequest_skips_new_rows_with_no_value_and_no_label()
    {
        var model = new PersonFormModel { FirstName = "Ada" };
        var filled = new ContactMethodFormRow { Kind = ContactMethodKind.Email, Value = "ada@example.com" };
        var untouched = new ContactMethodFormRow();
        var whitespaceOnly = new ContactMethodFormRow { Kind = ContactMethodKind.Phone, Value = "  ", Label = " " };
        var labelOnly = new ContactMethodFormRow { Kind = ContactMethodKind.Phone, Label = "Work" };
        model.ContactMethods.AddRange([untouched, filled, whitespaceOnly, labelOnly]);

        var request = model.ToUpdateRequest(out var sentRows);

        request.ContactMethods.Should().HaveCount(2);
        request.ContactMethods![0].Should().Be(new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com"));
        request.ContactMethods[1].Should().Be(new ContactMethodInput(null, ContactMethodKind.Phone, "Work", null),
            "a row with only a label is sent, so the service can say the value is missing");
        sentRows.Should().Equal(filled, labelOnly);
    }

    [Fact]
    public void A_saved_row_whose_text_was_cleared_is_still_sent_so_it_is_not_deleted_silently()
    {
        var id = Guid.NewGuid();
        var model = new PersonFormModel { FirstName = "Ada" };
        model.ContactMethods.Add(new ContactMethodFormRow { Id = id, Kind = ContactMethodKind.Email, Value = "" });

        var request = model.ToUpdateRequest();

        request.ContactMethods.Should().ContainSingle().Which.Should().Be(new ContactMethodInput(id, ContactMethodKind.Email, null, ""));
    }

    [Fact]
    public void ToUpdateRequest_splits_existing_and_new_tags()
    {
        var existing = Guid.NewGuid();
        var model = new PersonFormModel { FirstName = "Ada" };
        model.Tags.AddRange([new TagSelection(existing, "Chess"), new TagSelection(null, "Climbing")]);

        var request = model.ToUpdateRequest();

        request.TagIds.Should().Equal(existing);
        request.NewTagNames.Should().Equal("Climbing");
    }

    [Fact]
    public void ToCreateRequest_carries_the_contact_methods_and_tags_too()
    {
        var model = new PersonFormModel { FirstName = "Ada" };
        model.ContactMethods.Add(new ContactMethodFormRow { Kind = ContactMethodKind.Social, Value = "@ada" });
        model.Tags.Add(new TagSelection(null, "Mentor"));

        var request = model.ToCreateRequest(out var sentRows);

        request.ContactMethods.Should().ContainSingle().Which.Should().Be(new ContactMethodInput(null, ContactMethodKind.Social, null, "@ada"));
        request.NewTagNames.Should().Equal("Mentor");
        sentRows.Should().ContainSingle();
    }

    [Fact]
    public void Nothing_is_trimmed_before_it_is_sent()
    {
        var model = new PersonFormModel { FirstName = "  Ada " };
        model.ContactMethods.Add(new ContactMethodFormRow { Kind = ContactMethodKind.Email, Value = " ada@example.com ", Label = " Work " });

        var request = model.ToUpdateRequest();

        request.FirstName.Should().Be("  Ada ");
        request.ContactMethods.Should().ContainSingle().Which.Should().Be(
            new ContactMethodInput(null, ContactMethodKind.Email, " Work ", " ada@example.com "));
    }

    [Fact]
    public void ToCreateRequest_and_ToUpdateRequest_carry_birthday_reminder_settings()
    {
        var model = new PersonFormModel
        {
            FirstName = "Ada",
            BirthdayReminderDisabled = true,
            BirthdayReminderLeadDays = 14,
        };

        var create = model.ToCreateRequest();
        create.BirthdayReminderDisabled.Should().BeTrue();
        create.BirthdayReminderLeadDays.Should().Be(14);

        var update = model.ToUpdateRequest();
        update.BirthdayReminderDisabled.Should().BeTrue();
        update.BirthdayReminderLeadDays.Should().Be(14);
    }

    [Fact]
    public void ToCreateRequest_and_ToUpdateRequest_carry_stay_in_touch_cadence()
    {
        var model = new PersonFormModel
        {
            FirstName = "Ada",
            StayInTouchCadenceDays = 30,
        };

        var create = model.ToCreateRequest();
        create.StayInTouchCadenceDays.Should().Be(30);

        var update = model.ToUpdateRequest();
        update.StayInTouchCadenceDays.Should().Be(30);
    }
}
