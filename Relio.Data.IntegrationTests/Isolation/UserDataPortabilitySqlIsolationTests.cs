using Microsoft.EntityFrameworkCore;
using Relio.Application.Metrics;
using Relio.Application.People;
using Relio.Application.Portability;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Portability;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class UserDataPortabilitySqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Export_json_is_scoped_to_the_current_owner_for_every_record_kind()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var graphA = await SeedGraphAsync(fixture, harness, harness.OwnerA.Id, "A");
        var graphB = await SeedGraphAsync(fixture, harness, harness.OwnerB.Id, "B");

        await using var ownerAScope = harness.AsOwnerA();
        var export = await new UserDataPortabilityService(
                ownerAScope.DbContext,
                ownerAScope.CurrentUser,
                harness.Clock)
            .ExportAsync();

        export.Profile.Id.Should().Be(graphA.ProfileId);
        export.People.Select(person => person.Id).Should().BeEquivalentTo(graphA.PersonIds);
        export.Tags.Select(tag => tag.Id).Should().Contain(graphA.TagId).And.NotContain(graphB.TagId);
        export.RelationshipTypes.Select(type => type.Id)
            .Should().Contain(graphA.RelationshipTypeId).And.NotContain(graphB.RelationshipTypeId);
        export.Interactions.Select(interaction => interaction.Id)
            .Should().Contain(graphA.InteractionId).And.NotContain(graphB.InteractionId);
        export.Notes.Select(note => note.Id).Should().Contain(graphA.NoteId).And.NotContain(graphB.NoteId);
        export.Reminders.Select(reminder => reminder.Id).Should().BeEquivalentTo(graphA.ReminderIds);
        export.ProductActivity!.Id.Should().Be(graphA.ProductActivityId);
        export.People.SelectMany(person => person.ContactMethods).Select(contact => contact.Id)
            .Should().BeEquivalentTo(graphA.ContactMethodIds);
        export.Interactions.SelectMany(interaction => interaction.Participants).Select(participant => participant.Id)
            .Should().BeEquivalentTo(graphA.ParticipantIds);
    }

    [SqlServerFact]
    public async Task Export_vCard_is_scoped_to_the_current_owner_and_contains_only_contact_fields()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        await SeedGraphAsync(fixture, harness, harness.OwnerA.Id, "A");
        await SeedGraphAsync(fixture, harness, harness.OwnerB.Id, "B");

        await using var ownerAScope = harness.AsOwnerA();
        var vCard = await new UserDataPortabilityService(
                ownerAScope.DbContext,
                ownerAScope.CurrentUser,
                harness.Clock)
            .ExportPeopleVCardAsync();

        vCard.Should().Contain("AdaA").And.Contain("ada-a@example.com").And.NotContain("AdaB")
            .And.NotContain("ada-b@example.com").And.NotContain("private profile detail");
    }

    [SqlServerFact]
    public async Task Restore_remaps_the_full_graph_to_a_fresh_account_and_does_not_import_product_activity()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var source = await SeedGraphAsync(fixture, harness, harness.OwnerA.Id, "A");
        var snapshotClock = CreateSnapshotClock(source);
        ProductActivity? destinationActivityBefore;
        await using (var beforeRestore = fixture.CreateDbContext())
        {
            destinationActivityBefore = await beforeRestore.Set<ProductActivity>().AsNoTracking()
                .SingleOrDefaultAsync(activity => activity.OwnerId == harness.OwnerB.Id);
        }

        UserDataExportDocument export;
        await using (var ownerAScope = harness.AsOwnerA())
        {
            export = await new UserDataPortabilityService(
                    ownerAScope.DbContext,
                    ownerAScope.CurrentUser,
                    snapshotClock)
                .ExportAsync();
        }

        await using (var ownerBScope = harness.AsOwnerB())
        {
            var restore = await new UserDataPortabilityService(
                    ownerBScope.DbContext,
                    ownerBScope.CurrentUser,
                    snapshotClock)
                .RestoreAsync(export);

            restore.Should().Be(new UserDataRestoreResult(
                export.People.Count,
                export.Interactions.Count,
                export.Notes.Count,
                export.Reminders.Count,
                export.Tags.Count,
                export.RelationshipTypes.Count));
        }

        await using var verify = fixture.CreateDbContext();
        var restoredPeople = await verify.People.AsNoTracking()
            .Where(person => person.OwnerId == harness.OwnerB.Id)
            .OrderBy(person => person.FirstName)
            .ToListAsync();
        restoredPeople.Should().HaveCount(2);
        restoredPeople.Select(person => person.Id).Should().NotContain(source.PersonIds);
        restoredPeople.Should().Contain(person => person.IsArchived);
        var restoredActive = restoredPeople.Single(person => !person.IsArchived);
        restoredActive.RelationshipTypeId.Should().NotBe(source.RelationshipTypeId);
        restoredActive.Details.Should().Be("A private profile detail.");
        restoredActive.BirthdayDay.Should().Be(29);
        restoredActive.BirthdayMonth.Should().Be(2);

        var restoredContactMethods = await verify.ContactMethods.AsNoTracking()
            .Where(contact => contact.OwnerId == harness.OwnerB.Id)
            .ToListAsync();
        restoredContactMethods.Should().HaveCount(3);
        restoredContactMethods.Select(contact => contact.Id).Should().NotContain(source.ContactMethodIds);
        restoredContactMethods.Single(contact => contact.Kind == ContactMethodKind.Email)
            .NormalizedValue.Should().Be("ada-a@example.com");

        var restoredTag = await verify.Tags.AsNoTracking()
            .SingleAsync(tag => tag.OwnerId == harness.OwnerB.Id);
        restoredTag.Id.Should().NotBe(source.TagId);
        var restoredTypes = await verify.RelationshipTypes.AsNoTracking()
            .Where(type => type.OwnerId == harness.OwnerB.Id)
            .ToListAsync();
        restoredTypes.Should().HaveCount(export.RelationshipTypes.Count);
        restoredTypes.Select(type => type.Id).Should().NotContain(source.RelationshipTypeId);

        var restoredInteraction = await verify.Interactions.AsNoTracking()
            .Where(interaction => interaction.OwnerId == harness.OwnerB.Id)
            .Include(interaction => interaction.Participants)
            .SingleAsync();
        restoredInteraction.Id.Should().NotBe(source.InteractionId);
        restoredInteraction.Description.Should().Be("A private conversation with both people.");
        restoredInteraction.Participants.Select(participant => participant.PersonId)
            .Should().BeEquivalentTo(restoredPeople.Select(person => person.Id));
        restoredInteraction.Participants.Select(participant => participant.Id)
            .Should().NotContain(source.ParticipantIds);

        var restoredNote = await verify.Notes.AsNoTracking()
            .SingleAsync(note => note.OwnerId == harness.OwnerB.Id);
        restoredNote.Id.Should().NotBe(source.NoteId);
        restoredNote.Text.Should().Be("A private note edited after creation.");
        restoredNote.PersonId.Should().Be(restoredActive.Id);

        var restoredReminders = await verify.Reminders.AsNoTracking()
            .Where(reminder => reminder.OwnerId == harness.OwnerB.Id)
            .ToListAsync();
        restoredReminders.Should().HaveCount(2);
        restoredReminders.Select(reminder => reminder.Id).Should().NotContain(source.ReminderIds);
        restoredReminders.Should().Contain(reminder => reminder.IsCompleted && reminder.CompletedAtUtc != null);
        restoredReminders.Should().Contain(reminder => reminder.SnoozedUntilDate != null);

        var restoredProfile = await verify.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == harness.OwnerB.Id);
        restoredProfile.Id.Should().NotBe(source.ProfileId);
        restoredProfile.DisplayName.Should().Be("Owner A profile");
        restoredProfile.TimeZoneId.Should().Be("Pacific/Kiritimati");
        restoredProfile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
        var destinationActivitiesAfter = await verify.Set<ProductActivity>().AsNoTracking()
            .Where(activity => activity.OwnerId == harness.OwnerB.Id)
            .ToListAsync();
        if (destinationActivityBefore is null)
        {
            destinationActivitiesAfter.Should().BeEmpty();
        }
        else
        {
            destinationActivitiesAfter.Should().ContainSingle();
            destinationActivitiesAfter.Single().Should().BeEquivalentTo(destinationActivityBefore);
        }

        (await verify.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(activity => activity.OwnerId == harness.OwnerA.Id)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Restore_rejects_every_broken_graph_reference_without_changing_registration_data()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var source = await SeedGraphAsync(fixture, harness, harness.OwnerA.Id, "A");
        var snapshotClock = CreateSnapshotClock(source);
        UserDataExportDocument export;
        await using (var ownerAScope = harness.AsOwnerA())
        {
            export = await new UserDataPortabilityService(
                    ownerAScope.DbContext,
                    ownerAScope.CurrentUser,
                    snapshotClock)
                .ExportAsync();
        }

        var invalidRelationshipType = export with
        {
            People = export.People
                .Select((person, index) => index == 0
                    ? person with { RelationshipTypeId = Guid.NewGuid() }
                    : person)
                .ToArray(),
        };
        var invalidTag = export with
        {
            People = export.People
                .Select((person, index) => index == 0
                    ? person with { TagIds = person.TagIds.Append(Guid.NewGuid()).ToArray() }
                    : person)
                .ToArray(),
        };
        var invalidParticipant = export with
        {
            Interactions = export.Interactions
                .Select(interaction => interaction with
                {
                    Participants = interaction.Participants
                        .Select(participant => participant with { PersonId = Guid.NewGuid() })
                        .ToArray(),
                })
                .ToArray(),
        };
        var invalidNote = export with
        {
            Notes = export.Notes
                .Select(note => note with { PersonId = Guid.NewGuid() })
                .ToArray(),
        };
        var invalidReminder = export with
        {
            Reminders = export.Reminders
                .Select(reminder => reminder with { PersonId = Guid.NewGuid() })
                .ToArray(),
        };
        var invalidDocuments = new[]
        {
            invalidRelationshipType,
            invalidTag,
            invalidParticipant,
            invalidNote,
            invalidReminder,
        };

        await using var ownerBScope = harness.AsOwnerB();
        var profileBefore = await ownerBScope.DbContext.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == harness.OwnerB.Id);
        var typeNamesBefore = await ownerBScope.DbContext.RelationshipTypes.AsNoTracking()
            .Where(type => type.OwnerId == harness.OwnerB.Id)
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToArrayAsync();
        var service = new UserDataPortabilityService(
            ownerBScope.DbContext,
            ownerBScope.CurrentUser,
            snapshotClock);
        foreach (var invalid in invalidDocuments)
        {
            var restore = () => service.RestoreAsync(invalid);
            (await restore.Should().ThrowAsync<UserDataPortabilityException>())
                .Which.Errors.Should().Contain(UserDataPortabilityError.InvalidReference);
            ownerBScope.DbContext.ChangeTracker.Entries().Should().BeEmpty();
        }

        (await ownerBScope.DbContext.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == harness.OwnerB.Id)).Should().BeEquivalentTo(profileBefore);
        (await ownerBScope.DbContext.RelationshipTypes.AsNoTracking()
            .Where(type => type.OwnerId == harness.OwnerB.Id)
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToArrayAsync()).Should().Equal(typeNamesBefore);
        (await ownerBScope.DbContext.People.AsNoTracking()
            .AnyAsync(person => person.OwnerId == harness.OwnerB.Id)).Should().BeFalse();
    }

    [SqlServerFact]
    public async Task Restore_rejects_existing_owner_content_without_replacing_it()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var source = await SeedGraphAsync(fixture, harness, harness.OwnerA.Id, "A");
        var snapshotClock = CreateSnapshotClock(source);
        var existing = await SeedGraphAsync(fixture, harness, harness.OwnerB.Id, "B");
        UserDataExportDocument export;
        await using (var ownerAScope = harness.AsOwnerA())
        {
            export = await new UserDataPortabilityService(
                    ownerAScope.DbContext,
                    ownerAScope.CurrentUser,
                    snapshotClock)
                .ExportAsync();
        }

        await using var ownerBScope = harness.AsOwnerB();
        var service = new UserDataPortabilityService(
            ownerBScope.DbContext,
            ownerBScope.CurrentUser,
            snapshotClock);
        var restore = () => service.RestoreAsync(export);

        (await restore.Should().ThrowAsync<UserDataPortabilityException>())
            .Which.Errors.Should().Contain(UserDataPortabilityError.DestinationNotFresh);
        ownerBScope.DbContext.ChangeTracker.Entries().Should().BeEmpty();
        (await ownerBScope.DbContext.People.AsNoTracking()
            .CountAsync(person => person.OwnerId == harness.OwnerB.Id)).Should().Be(2);
        (await ownerBScope.DbContext.Notes.AsNoTracking()
            .SingleAsync(note => note.OwnerId == harness.OwnerB.Id)).Id.Should().Be(existing.NoteId);
        (await ownerBScope.DbContext.Tags.AsNoTracking()
            .SingleAsync(tag => tag.OwnerId == harness.OwnerB.Id)).Id.Should().Be(existing.TagId);
    }

    internal static Task SeedOwnerGraphAsync(
        SqlServerDatabaseFixture fixture,
        SqlIsolationTestHarness harness,
        string ownerId,
        string suffix) =>
        SeedGraphAsync(fixture, harness, ownerId, suffix);

    private static async Task<SeededGraph> SeedGraphAsync(
        SqlServerDatabaseFixture fixture,
        SqlIsolationTestHarness harness,
        string ownerId,
        string suffix)
    {
        await using var dbContext = fixture.CreateDbContext();
        var profile = await dbContext.UserProfiles.SingleAsync(item => item.OwnerId == ownerId);
        profile.TimeZoneId = "Pacific/Kiritimati";
        profile.DisplayName = $"Owner {suffix} profile";
        profile.OnboardingDismissed = false;
        profile.BirthdayRemindersEnabled = false;
        profile.DefaultBirthdayLeadDays = 4;
        profile.ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest;

        var relationshipType = await dbContext.RelationshipTypes
            .Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .FirstAsync();
        var seedInstantUtc = harness.Clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(seedInstantUtc);
        var tag = new Tag { OwnerId = ownerId, Name = $"Reflective {suffix}" };
        var active = new Person
        {
            OwnerId = ownerId,
            FirstName = suffix == "A" ? "AdaA" : "AdaB",
            LastName = "Lovelace",
            Nickname = "Enchantress",
            RelationshipTypeId = relationshipType.Id,
            BirthdayDay = 29,
            BirthdayMonth = 2,
            HowWeMet = "A shared project.",
            Details = "A private profile detail.",
            LastContactedOn = today,
            StayInTouchCadenceDays = 30,
            BirthdayReminderDisabled = true,
            BirthdayReminderLeadDays = 5,
        };
        active.Tags.Add(tag);
        active.ContactMethods.Add(NewContact(
            ownerId,
            active.Id,
            ContactMethodKind.Email,
            "Work",
            suffix == "A" ? "ada-a@example.com" : "ada-b@example.com",
            sortOrder: 0));
        active.ContactMethods.Add(NewContact(
            ownerId,
            active.Id,
            ContactMethodKind.Phone,
            "Mobile",
            suffix == "A" ? "+1 555 123 4567" : "+1 555 234 5678",
            sortOrder: 1));

        var archived = new Person
        {
            OwnerId = ownerId,
            FirstName = suffix == "A" ? "GraceA" : "GraceB",
            LastName = "Hopper",
            IsArchived = true,
        };
        archived.Tags.Add(tag);
        archived.ContactMethods.Add(NewContact(
            ownerId,
            archived.Id,
            ContactMethodKind.Address,
            "Home",
            "1 Main Street",
            sortOrder: 0));

        var interaction = new Interaction
        {
            OwnerId = ownerId,
            OccurredOn = today.AddDays(-2),
            Kind = InteractionKind.Meeting,
            Description = "A private conversation with both people.",
        };
        var participants = new[]
        {
            new InteractionParticipant { OwnerId = ownerId, InteractionId = interaction.Id, PersonId = active.Id },
            new InteractionParticipant { OwnerId = ownerId, InteractionId = interaction.Id, PersonId = archived.Id },
        };
        var note = new Note
        {
            OwnerId = ownerId,
            PersonId = active.Id,
            Text = "A private note edited after creation.",
            IsPinned = true,
        };
        var completedReminder = new Reminder
        {
            OwnerId = ownerId,
            PersonId = active.Id,
            Title = "Follow up after the meeting",
            DueDate = today.AddDays(-5),
            Frequency = ReminderFrequency.Monthly,
            IsCompleted = true,
            LastDeliveredDate = today.AddDays(-5),
        };
        var snoozedReminder = new Reminder
        {
            OwnerId = ownerId,
            PersonId = archived.Id,
            Title = "Check in later",
            DueDate = today.AddDays(3),
            Frequency = ReminderFrequency.Once,
            SnoozedUntilDate = today.AddDays(7),
            LastDeliveredDate = today,
        };
        var cohortStartedOnUtc = today.AddDays(-58);
        var productActivity = await dbContext.Set<ProductActivity>()
            .SingleOrDefaultAsync(activity => activity.OwnerId == ownerId);
        var isNewProductActivity = productActivity is null;
        productActivity ??= new ProductActivity { OwnerId = ownerId };
        productActivity.CohortStartedOnUtc = cohortStartedOnUtc;
        productActivity.LastActiveOnUtc = today;
        productActivity.ReturnedInDays30To59 = true;
        productActivity.RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(cohortStartedOnUtc);

        dbContext.Tags.Add(tag);
        dbContext.People.AddRange(active, archived);
        dbContext.Interactions.Add(interaction);
        dbContext.InteractionParticipants.AddRange(participants);
        dbContext.Notes.Add(note);
        dbContext.Reminders.AddRange(completedReminder, snoozedReminder);
        if (isNewProductActivity)
        {
            dbContext.Set<ProductActivity>().Add(productActivity);
        }
        await dbContext.SaveChangesAsync();

        archived.ArchivedAtUtc = archived.CreatedAtUtc;
        completedReminder.CompletedAtUtc = completedReminder.CreatedAtUtc;
        await dbContext.SaveChangesAsync();

        var latestAuditAtUtc = dbContext.ChangeTracker.Entries<IOwnedEntity>()
            .Where(entry => entry.Entity.OwnerId == ownerId)
            .Select(entry => entry.Entity.UpdatedAtUtc)
            .Max();

        return new SeededGraph(
            profile.Id,
            relationshipType.Id,
            tag.Id,
            active.Id,
            archived.Id,
            interaction.Id,
            participants.Select(participant => participant.Id).ToArray(),
            note.Id,
            [completedReminder.Id, snoozedReminder.Id],
            active.ContactMethods.Select(contact => contact.Id)
                .Concat(archived.ContactMethods.Select(contact => contact.Id))
                .ToArray(),
            productActivity.Id,
            latestAuditAtUtc);
    }

    private static TimeProvider CreateSnapshotClock(SeededGraph graph)
    {
        var instant = DateTime.SpecifyKind(graph.LatestAuditAtUtc.AddSeconds(1), DateTimeKind.Utc);
        return new FixedTimeProvider(new DateTimeOffset(instant, TimeSpan.Zero));
    }

    private static ContactMethod NewContact(
        string ownerId,
        Guid personId,
        ContactMethodKind kind,
        string? label,
        string value,
        int sortOrder) =>
        new()
        {
            OwnerId = ownerId,
            PersonId = personId,
            Kind = kind,
            Label = label,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(kind, value),
            SortOrder = sortOrder,
        };

    private sealed record SeededGraph(
        Guid ProfileId,
        Guid RelationshipTypeId,
        Guid TagId,
        Guid ActivePersonId,
        Guid ArchivedPersonId,
        Guid InteractionId,
        Guid[] ParticipantIds,
        Guid NoteId,
        Guid[] ReminderIds,
        Guid[] ContactMethodIds,
        Guid ProductActivityId,
        DateTime LatestAuditAtUtc)
    {
        public Guid[] PersonIds => [ActivePersonId, ArchivedPersonId];
    }
}
