using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.Metrics;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Application.Portability;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Exercises portability through the real authenticated app, browser download/import controls,
/// and registered data service graph. Each account is new; the shared demo account is untouched.
/// </summary>
[Collection(RelioAppCollection.Name)]
public sealed class UserDataPortabilityTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Full_json_and_contact_only_vcard_round_trip_between_fresh_accounts()
    {
        var sourcePage = await fixture.NewPageAsync();
        var destinationPage = await fixture.NewPageAsync();
        try
        {
            await RoundTripAsync(sourcePage, destinationPage);
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(sourcePage);
            await RelioAppFixture.ClosePageAsync(destinationPage);
        }
    }

    private async Task RoundTripAsync(IPage sourcePage, IPage destinationPage)
    {
        var sourceEmail = NewEmail("portability-source");
        var destinationEmail = NewEmail("portability-destination");
        await RegisterAsync(sourcePage, sourceEmail, StrongPassword);
        await RegisterAsync(destinationPage, destinationEmail, StrongPassword);
        var sourceUser = (await GetUserAsync(fixture.App, sourceEmail))!;
        var destinationUser = (await GetUserAsync(fixture.App, destinationEmail))!;

        var source = await SeedSourceGraphAsync(fixture.App, sourceUser.Id);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(sourcePage, "/settings/data");

        var jsonDownload = await sourcePage.RunAndWaitForDownloadAsync(
            () => sourcePage.GetByTestId("data-export-json").ClickAsync());
        jsonDownload.SuggestedFilename.Should().Be("relio-data.json");
        var jsonBytes = await ReadDownloadAsync(jsonDownload);
        var jsonText = Encoding.UTF8.GetString(jsonBytes);
        jsonText.Should().Contain("A private note from the source account.");
        jsonText.Should().Contain("A private conversation between both profiles.");
        jsonText.Should().Contain("A narrative that belongs in complete JSON only.");
        jsonText.Should().NotContain("source-unsubscribe-secret");
        jsonText.Should().NotContain("OwnerId");
        using (var json = JsonDocument.Parse(jsonBytes))
        {
            json.RootElement.GetProperty("formatVersion").GetInt32()
                .Should().Be(UserDataExportDocument.CurrentFormatVersion);
            json.RootElement.GetProperty("people").GetArrayLength().Should().Be(2);
            json.RootElement.GetProperty("productActivity").ValueKind.Should().Be(JsonValueKind.Object);
        }

        var vCardDownload = await sourcePage.RunAndWaitForDownloadAsync(
            () => sourcePage.GetByTestId("data-export-vcard").ClickAsync());
        vCardDownload.SuggestedFilename.Should().Be("relio-people.vcf");
        var vCardBytes = await ReadDownloadAsync(vCardDownload);
        var vCardText = Encoding.UTF8.GetString(vCardBytes);
        vCardText.Should().Contain("BEGIN:VCARD\r\nVERSION:4.0");
        vCardText.Should().NotContain("A private note from the source account.");
        vCardText.Should().NotContain("A private conversation between both profiles.");
        vCardText.Should().NotContain("A narrative that belongs in complete JSON only.");
        var parsedVCard = VCardReader.Read(vCardBytes);
        parsedVCard.People.Should().HaveCount(2, "archived people are included in the contact-only format");
        parsedVCard.People.SelectMany(person => person.ContactMethods).Should().Contain(
            contact => contact.Kind == ContactMethodKind.Email && contact.Value == source.Email);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(destinationPage, "/settings/data");
        var tampered = JsonNode.Parse(jsonBytes)!.AsObject();
        tampered["people"]!.AsArray()[0]!.AsObject()["relationshipTypeId"] = Guid.NewGuid().ToString();
        var invalidBytes = Encoding.UTF8.GetBytes(tampered.ToJsonString());
        await UploadJsonAsync(destinationPage, "untrusted-private-export.json", invalidBytes);
        await Expect(destinationPage.GetByTestId("data-restore-selection")).ToHaveTextAsync("A JSON export is selected.");
        await destinationPage.GetByTestId("data-restore-submit").ClickAsync();
        await Expect(destinationPage.GetByTestId("data-portability-message"))
            .ToContainTextAsync("Some records in the export don't link to other records in that same file.");
        await AssertDestinationUnchangedAsync(fixture.App, destinationUser.Id);

        await UploadJsonAsync(destinationPage, "valid-relio-export.json", jsonBytes);
        await Expect(destinationPage.GetByTestId("data-restore-selection")).ToHaveTextAsync("A JSON export is selected.");
        await destinationPage.GetByTestId("data-restore-submit").ClickAsync();
        await Expect(destinationPage.GetByTestId("data-portability-message"))
            .ToContainTextAsync("Restored 2 people, 1 interactions, 1 notes and 2 reminders.");
        await Expect(destinationPage.Locator("html[data-app-ready='true']")).ToBeVisibleAsync();
        destinationPage.Url.Should().EndWith("/settings/data", "the export and its values never enter the URL");

        using (var scope = fixture.App.CreateRealScope())
        {
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<RelioDbContext>();
            var restoredPeople = await db.People.AsNoTracking()
                .Where(person => person.OwnerId == destinationUser.Id)
                .Include(person => person.ContactMethods)
                .Include(person => person.Tags)
                .OrderBy(person => person.IsArchived)
                .ToListAsync();
            restoredPeople.Should().HaveCount(2);
            restoredPeople.Should().NotContain(person => person.Id == source.ActivePersonId || person.Id == source.ArchivedPersonId);
            restoredPeople.Should().ContainSingle(person => !person.IsArchived);
            restoredPeople.Should().ContainSingle(person => person.IsArchived);
            restoredPeople.Single(person => !person.IsArchived).BirthdayDay.Should().Be(29);
            restoredPeople.Single(person => !person.IsArchived).BirthdayMonth.Should().Be(2);
            restoredPeople.Single(person => !person.IsArchived).BirthdayYear.Should().BeNull();
            restoredPeople.Single(person => !person.IsArchived).StayInTouchCadenceDays.Should().Be(30);
            restoredPeople.Single(person => !person.IsArchived).BirthdayReminderDisabled.Should().BeTrue();
            restoredPeople.Single(person => !person.IsArchived).ContactMethods
                .OrderBy(contact => contact.SortOrder)
                .Select(contact => contact.Kind)
                .Should().Equal(ContactMethodKind.Email, ContactMethodKind.Phone);
            restoredPeople.Single(person => !person.IsArchived).ContactMethods
                .Single(contact => contact.Kind == ContactMethodKind.Email)
                .NormalizedValue.Should().Be(source.Email);

            var types = await db.RelationshipTypes.AsNoTracking()
                .Where(type => type.OwnerId == destinationUser.Id)
                .ToListAsync();
            types.Should().ContainSingle().Which.Name.Should().Be("Confidant");
            types.Select(type => type.Id).Should().NotContain(source.RelationshipTypeId);
            (await db.Tags.AsNoTracking().SingleAsync(tag => tag.OwnerId == destinationUser.Id))
                .Id.Should().NotBe(source.TagId);

            var interaction = await db.Interactions.AsNoTracking()
                .SingleAsync(item => item.OwnerId == destinationUser.Id);
            interaction.Description.Should().Be("A private conversation between both profiles.");
            var participants = await db.InteractionParticipants.AsNoTracking()
                .Where(item => item.OwnerId == destinationUser.Id && item.InteractionId == interaction.Id)
                .ToListAsync();
            participants.Should().HaveCount(2);
            participants.Select(participant => participant.PersonId)
                .Should().BeEquivalentTo(restoredPeople.Select(person => person.Id));

            var note = await db.Notes.AsNoTracking().SingleAsync(item => item.OwnerId == destinationUser.Id);
            note.Text.Should().Be("A private note from the source account.");
            note.IsPinned.Should().BeTrue();
            note.PersonId.Should().NotBe(source.ActivePersonId);

            var reminders = await db.Reminders.AsNoTracking()
                .Where(item => item.OwnerId == destinationUser.Id)
                .OrderBy(item => item.Title)
                .ToListAsync();
            reminders.Should().HaveCount(2);
            reminders.Should().ContainSingle(item => item.IsCompleted && item.Frequency == ReminderFrequency.Monthly);
            reminders.Should().ContainSingle(item =>
                !item.IsCompleted
                && item.SnoozedUntilDate == source.SnoozedUntilDate
                && item.LastDeliveredDate == source.LastDeliveredDate);
            reminders.Should().OnlyContain(item => restoredPeople.Any(person => person.Id == item.PersonId));

            var profile = await db.UserProfiles.AsNoTracking()
                .SingleAsync(item => item.OwnerId == destinationUser.Id);
            profile.TimeZoneId.Should().Be("Pacific/Kiritimati");
            profile.DisplayName.Should().Be("Source Profile");
            profile.BirthdayRemindersEnabled.Should().BeFalse();
            profile.DefaultBirthdayLeadDays.Should().Be(5);
            profile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
            profile.UnsubscribeToken.Should().NotBeNullOrWhiteSpace().And.NotBe("source-unsubscribe-secret");

            var identityUser = await services.GetRequiredService<UserManager<RelioUser>>()
                .FindByIdAsync(destinationUser.Id);
            identityUser!.Email.Should().Be(destinationEmail);
            (await db.Set<ProductActivity>().AsNoTracking()
                .AnyAsync(item => item.OwnerId == destinationUser.Id))
                .Should().BeFalse("source analytics is export-only and collection is off by default");
        }

    }

    private static async Task<SourceGraph> SeedSourceGraphAsync(RelioWebAppFactory factory, string ownerId)
    {
        using var scope = factory.CreateRealScope();
        var db = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var cohortStart = today.AddDays(-65);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var email = $"source-{suffix}@example.com";

        var profile = await db.UserProfiles.SingleAsync(item => item.OwnerId == ownerId);
        profile.TimeZoneId = "Pacific/Kiritimati";
        profile.DisplayName = "Source Profile";
        profile.BirthdayRemindersEnabled = false;
        profile.DefaultBirthdayLeadDays = 5;
        profile.ReminderEmailDelivery = ReminderEmailDelivery.Immediate;
        profile.UnsubscribeToken = "source-unsubscribe-secret";

        var relationshipTypes = await db.RelationshipTypes
            .Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.SortOrder)
            .ToListAsync();
        var relationshipType = relationshipTypes[2];
        relationshipType.Name = "Confidant";
        db.RelationshipTypes.RemoveRange(relationshipTypes.Where(item => item.Id != relationshipType.Id));

        var tag = new Tag { OwnerId = ownerId, Name = "Portability" };
        db.Tags.Add(tag);

        var active = new Person
        {
            OwnerId = ownerId,
            FirstName = $"Ada{suffix}",
            LastName = "Lovelace",
            Nickname = "Ada",
            RelationshipTypeId = relationshipType.Id,
            BirthdayMonth = 2,
            BirthdayDay = 29,
            HowWeMet = "A narrative that belongs in complete JSON only.",
            Details = "Private source details.",
            LastContactedOn = today.AddDays(-1),
            StayInTouchCadenceDays = 30,
            BirthdayReminderDisabled = true,
            BirthdayReminderLeadDays = 4,
        };
        active.Tags.Add(tag);
        active.ContactMethods.Add(NewContact(ownerId, active.Id, ContactMethodKind.Email, "Work", email, 0));
        active.ContactMethods.Add(NewContact(ownerId, active.Id, ContactMethodKind.Phone, "Mobile", "+44 7700 900123", 1));

        var archived = new Person
        {
            OwnerId = ownerId,
            FirstName = $"Grace{suffix}",
            LastName = "Hopper",
        };
        archived.Tags.Add(tag);
        archived.ContactMethods.Add(NewContact(ownerId, archived.Id, ContactMethodKind.Address, "Home", "1 Example Street", 0));
        db.People.AddRange(active, archived);

        var interaction = new Interaction
        {
            OwnerId = ownerId,
            OccurredOn = today.AddDays(-2),
            Kind = InteractionKind.Meeting,
            Description = "A private conversation between both profiles.",
        };
        db.Interactions.Add(interaction);
        db.InteractionParticipants.AddRange(
            new InteractionParticipant { OwnerId = ownerId, InteractionId = interaction.Id, PersonId = active.Id },
            new InteractionParticipant { OwnerId = ownerId, InteractionId = interaction.Id, PersonId = archived.Id });
        db.Notes.Add(new Note
        {
            OwnerId = ownerId,
            PersonId = active.Id,
            Text = "A private note from the source account.",
            IsPinned = true,
        });
        db.Reminders.AddRange(
            new Reminder
            {
                OwnerId = ownerId,
                PersonId = active.Id,
                Title = "Completed monthly reminder",
                DueDate = today.AddDays(-30),
                Frequency = ReminderFrequency.Monthly,
                LastDeliveredDate = today.AddDays(-30),
            },
            new Reminder
            {
                OwnerId = ownerId,
                PersonId = archived.Id,
                Title = "Snoozed one-off reminder",
                DueDate = today.AddDays(2),
                Frequency = ReminderFrequency.Once,
                SnoozedUntilDate = today.AddDays(5),
                LastDeliveredDate = today.AddDays(-1),
            });
        db.Set<ProductActivity>().Add(new ProductActivity
        {
            OwnerId = ownerId,
            CohortStartedOnUtc = cohortStart,
            LastActiveOnUtc = cohortStart.AddDays(40),
            ReturnedInDays30To59 = true,
            RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(cohortStart),
        });

        await db.SaveChangesAsync();
        archived.IsArchived = true;
        archived.ArchivedAtUtc = clock.GetUtcNow().UtcDateTime;
        var completedReminder = await db.Reminders.SingleAsync(item =>
            item.OwnerId == ownerId && item.Title == "Completed monthly reminder");
        completedReminder.IsCompleted = true;
        completedReminder.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();

        return new SourceGraph(
            active.Id,
            archived.Id,
            relationshipType.Id,
            tag.Id,
            email,
            today.AddDays(5),
            today.AddDays(-1));
    }

    private static ContactMethod NewContact(
        string ownerId,
        Guid personId,
        ContactMethodKind kind,
        string? label,
        string value,
        int sortOrder)
    {
        var normalized = ContactMethodRules.NormalizeValue(kind, value);
        return new ContactMethod
        {
            OwnerId = ownerId,
            PersonId = personId,
            Kind = kind,
            Label = label,
            Value = normalized,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(kind, normalized),
            SortOrder = sortOrder,
        };
    }

    private static async Task UploadJsonAsync(IPage page, string name, byte[] bytes) =>
        await page.Locator("[data-testid='data-restore-file']").SetInputFilesAsync(new FilePayload
        {
            Name = name,
            MimeType = "application/json",
            Buffer = bytes,
        });

    private static async Task<byte[]> ReadDownloadAsync(IDownload download)
    {
        await using var stream = await download.CreateReadStreamAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static async Task AssertDestinationUnchangedAsync(RelioWebAppFactory factory, string ownerId)
    {
        using var scope = factory.CreateRealScope();
        var db = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        (await db.People.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await db.Tags.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await db.Interactions.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await db.Notes.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await db.Reminders.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await db.RelationshipTypes.AsNoTracking().Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.SortOrder).Select(item => item.Name).ToArrayAsync())
            .Should().Equal(RelationshipType.DefaultNames);
    }

    private sealed record SourceGraph(
        Guid ActivePersonId,
        Guid ArchivedPersonId,
        Guid RelationshipTypeId,
        Guid TagId,
        string Email,
        DateOnly SnoozedUntilDate,
        DateOnly LastDeliveredDate);
}
