using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Ownership;
using Relio.Application.Portability;
using Relio.Application.Security;
using Relio.Data.Portability;
using Relio.Data.Tests.People;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data.Tests.Portability;

public sealed class UserDataPortabilityServiceTests
{
    private const string SourceOwner = "portability-source";
    private const string DestinationOwner = "portability-destination";
    private const string OtherOwner = "portability-other";

    private static readonly DateTimeOffset Start = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Export_and_restore_preserves_the_full_graph_and_history_but_remaps_ids_and_local_settings()
    {
        var clock = new FakeTimeProvider(Start);
        var saveCounter = new CountingSaveChangesInterceptor();
        await using var dbContext = CreateDbContext(clock, saveCounter);
        var source = await SeedGraphAsync(dbContext, clock, SourceOwner);
        await SeedRegistrationOnlyAccountAsync(dbContext, DestinationOwner);
        saveCounter.StartCounting();

        var export = await CreateService(dbContext, SourceOwner, clock).ExportAsync();
        var result = await CreateService(dbContext, DestinationOwner, clock).RestoreAsync(export);

        result.Should().Be(new UserDataRestoreResult(2, 1, 1, 2, 1, 1));
        saveCounter.SaveChangesCount.Should().Be(1, "the complete destination restore is one unit of work");

        export.People.Should().HaveCount(2);
        export.People.Should().Contain(person => person.IsArchived);
        export.People.Single(person => person.IsArchived).ArchivedAtUtc.Should().BeNull();
        export.People.Single(person => !person.IsArchived).ContactMethods
            .Select(contact => contact.SortOrder).Should().Equal(2, 7);
        export.Notes.Should().ContainSingle().Which.IsPinned.Should().BeTrue();
        export.Interactions.Should().ContainSingle().Which.Participants.Should().HaveCount(2);
        export.Reminders.Select(reminder => reminder.Frequency).Should().Contain(ReminderFrequency.Monthly);
        export.Reminders.Should().Contain(reminder => reminder.IsCompleted && reminder.CompletedAtUtc != null);
        export.Reminders.Should().Contain(reminder => reminder.SnoozedUntilDate != null);
        export.ProductActivity.Should().NotBeNull();

        dbContext.ChangeTracker.Clear();
        var restoredPeople = await dbContext.People.AsNoTracking()
            .Where(person => person.OwnerId == DestinationOwner)
            .OrderBy(person => person.FirstName)
            .ToListAsync();
        restoredPeople.Should().HaveCount(2);
        restoredPeople.Should().Contain(person => person.IsArchived);
        restoredPeople.Select(person => person.Id).Should().NotContain(source.PersonIds);
        var restoredActive = restoredPeople.Single(person => !person.IsArchived);
        restoredActive.RelationshipTypeId.Should().NotBe(source.RelationshipTypeId);
        restoredActive.BirthdayDay.Should().Be(29);
        restoredActive.BirthdayMonth.Should().Be(2);
        restoredActive.BirthdayYear.Should().BeNull();
        restoredActive.StayInTouchCadenceDays.Should().Be(30);
        restoredActive.BirthdayReminderDisabled.Should().BeTrue();
        restoredActive.BirthdayReminderLeadDays.Should().Be(5);
        restoredActive.LastContactedOn.Should().Be(DateOnly.FromDateTime(Start.UtcDateTime).AddDays(58));

        var restoredContacts = await dbContext.ContactMethods.AsNoTracking()
            .Where(contact => contact.OwnerId == DestinationOwner)
            .OrderBy(contact => contact.PersonId)
            .ThenBy(contact => contact.SortOrder)
            .ToListAsync();
        restoredContacts.Should().HaveCount(3);
        restoredContacts.Select(contact => contact.Id).Should().NotContain(source.ContactMethodIds);
        restoredContacts.Single(contact => contact.Kind == ContactMethodKind.Email)
            .NormalizedValue.Should().Be("ada@example.com");
        restoredContacts.Where(contact => contact.PersonId == restoredActive.Id)
            .OrderBy(contact => contact.SortOrder)
            .Select(contact => contact.SortOrder)
            .Should().Equal(2, 7);
        var restoredArchived = restoredPeople.Single(person => person.IsArchived);
        restoredArchived.ArchivedAtUtc.Should().BeNull();
        restoredContacts.Where(contact => contact.PersonId == restoredArchived.Id)
            .Select(contact => contact.SortOrder)
            .Should().Equal(4);

        var restoredTypes = await dbContext.RelationshipTypes.AsNoTracking()
            .Where(type => type.OwnerId == DestinationOwner)
            .ToListAsync();
        restoredTypes.Should().ContainSingle().Which.Name.Should().Be("Trusted");
        restoredTypes.Select(type => type.Id).Should().NotContain(source.RelationshipTypeId);

        var restoredTags = await dbContext.Tags.AsNoTracking()
            .Where(tag => tag.OwnerId == DestinationOwner)
            .ToListAsync();
        restoredTags.Should().ContainSingle().Which.Name.Should().Be("Reflective");
        restoredTags.Select(tag => tag.Id).Should().NotContain(source.TagId);

        var restoredInteraction = await dbContext.Interactions.AsNoTracking()
            .Where(interaction => interaction.OwnerId == DestinationOwner)
            .Include(interaction => interaction.Participants)
            .SingleAsync();
        restoredInteraction.Id.Should().NotBe(source.InteractionId);
        restoredInteraction.Description.Should().Be("A private conversation with both people.");
        restoredInteraction.Participants.Select(participant => participant.PersonId)
            .Should().BeEquivalentTo(restoredPeople.Select(person => person.Id));
        restoredInteraction.Participants.Select(participant => participant.Id)
            .Should().NotContain(source.ParticipantIds);

        var restoredNote = await dbContext.Notes.AsNoTracking()
            .SingleAsync(note => note.OwnerId == DestinationOwner);
        restoredNote.Id.Should().NotBe(source.NoteId);
        restoredNote.Text.Should().Be("A private note, edited after it was created.");
        restoredNote.IsPinned.Should().BeTrue();
        restoredNote.CreatedAtUtc.Should().Be(export.Notes.Single().CreatedAtUtc);
        restoredNote.UpdatedAtUtc.Should().Be(export.Notes.Single().UpdatedAtUtc);

        var restoredReminders = await dbContext.Reminders.AsNoTracking()
            .Where(reminder => reminder.OwnerId == DestinationOwner)
            .ToListAsync();
        restoredReminders.Select(reminder => reminder.Id).Should().NotContain(source.ReminderIds);
        restoredReminders.Single(reminder => reminder.IsCompleted).CompletedAtUtc
            .Should().Be(export.Reminders.Single(reminder => reminder.IsCompleted).CompletedAtUtc);
        restoredReminders.Single(reminder => reminder.SnoozedUntilDate is not null).LastDeliveredDate
            .Should().Be(new DateOnly(2024, 2, 10));

        var restoredProfile = await dbContext.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == DestinationOwner);
        restoredProfile.Id.Should().NotBe(export.Profile.Id);
        restoredProfile.TimeZoneId.Should().Be("Pacific/Kiritimati");
        restoredProfile.DisplayName.Should().Be("Source profile");
        restoredProfile.OnboardingDismissed.Should().BeFalse();
        restoredProfile.BirthdayRemindersEnabled.Should().BeFalse();
        restoredProfile.DefaultBirthdayLeadDays.Should().Be(4);
        restoredProfile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
        restoredProfile.UnsubscribeToken.Should().NotBeNullOrWhiteSpace().And.NotBe("destination-token");
        restoredProfile.UnsubscribeTokenVerifier.Should().Be(UnsubscribeTokenHash.Compute(restoredProfile.UnsubscribeToken));

        (await dbContext.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(activity => activity.OwnerId == DestinationOwner))
            .Should().BeFalse("instance-specific observations are export-only");
        (await dbContext.UserProfiles.AsNoTracking().CountAsync(profile => profile.OwnerId == DestinationOwner))
            .Should().Be(1, "the restored account keeps its registration profile row rather than copying the source id");
        restoredPeople.Select(person => person.OwnerId).Should().OnlyContain(owner => owner == DestinationOwner);
    }

    [Fact]
    public async Task Export_is_scoped_to_the_current_owner_for_every_owned_record_kind()
    {
        var clock = new FakeTimeProvider(Start);
        await using var dbContext = CreateDbContext(clock);
        var source = await SeedGraphAsync(dbContext, clock, SourceOwner);
        var other = await SeedGraphAsync(dbContext, clock, OtherOwner);
        var otherOnly = new Person { OwnerId = OtherOwner, FirstName = "Foreign only" };
        otherOnly.ContactMethods.Add(NewContact(
            OtherOwner,
            ContactMethodKind.Email,
            "Private",
            "foreign-only@example.net",
            0));
        dbContext.People.Add(otherOnly);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var export = await CreateService(dbContext, SourceOwner, clock).ExportAsync();
        var vCard = await CreateService(dbContext, SourceOwner, clock).ExportPeopleVCardAsync();

        export.Profile.Id.Should().Be(source.ProfileId);
        export.People.Select(person => person.Id).Should().BeEquivalentTo(source.PersonIds);
        export.Tags.Select(tag => tag.Id).Should().Contain(source.TagId).And.NotContain(other.TagId);
        export.RelationshipTypes.Select(type => type.Id).Should().Contain(source.RelationshipTypeId).And.NotContain(other.RelationshipTypeId);
        export.Interactions.Select(interaction => interaction.Id).Should().Contain(source.InteractionId).And.NotContain(other.InteractionId);
        export.Notes.Select(note => note.Id).Should().Contain(source.NoteId).And.NotContain(other.NoteId);
        export.Reminders.Select(reminder => reminder.Id).Should().BeEquivalentTo(source.ReminderIds);
        export.ProductActivity!.Id.Should().Be(source.ProductActivityId);
        export.People.SelectMany(person => person.ContactMethods).Select(contact => contact.Id)
            .Should().BeEquivalentTo(source.ContactMethodIds);
        export.Interactions.SelectMany(interaction => interaction.Participants).Select(participant => participant.Id)
            .Should().BeEquivalentTo(source.ParticipantIds);
        vCard.Should().Contain("ada@example.com").And.NotContain("foreign-only@example.net");
    }

    [Fact]
    public async Task Invalid_graph_restore_leaves_registration_defaults_and_profile_unchanged()
    {
        var clock = new FakeTimeProvider(Start);
        await using var dbContext = CreateDbContext(clock);
        await SeedRegistrationOnlyAccountAsync(dbContext, DestinationOwner);
        var originalProfile = await dbContext.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == DestinationOwner);
        var originalTypes = await dbContext.RelationshipTypes.AsNoTracking()
            .Where(type => type.OwnerId == DestinationOwner)
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToArrayAsync();
        var foreignType = new RelationshipType { OwnerId = OtherOwner, Name = "Foreign type", SortOrder = 0 };
        var foreignTag = new Tag { OwnerId = OtherOwner, Name = "Foreign tag" };
        dbContext.RelationshipTypes.Add(foreignType);
        dbContext.Tags.Add(foreignTag);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var invalid = ValidEmptyDocument() with
        {
            People =
            [
                PersonSnapshotFor(Guid.NewGuid()) with
                {
                    RelationshipTypeId = foreignType.Id,
                    TagIds = [foreignTag.Id],
                },
            ],
        };

        var act = () => CreateService(dbContext, DestinationOwner, clock).RestoreAsync(invalid);

        (await act.Should().ThrowAsync<UserDataPortabilityException>())
            .Which.Errors.Should().Contain(UserDataPortabilityError.InvalidReference);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        (await dbContext.People.AsNoTracking().CountAsync(person => person.OwnerId == DestinationOwner)).Should().Be(0);
        (await dbContext.Tags.AsNoTracking().CountAsync(tag => tag.OwnerId == DestinationOwner)).Should().Be(0);
        (await dbContext.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == DestinationOwner)).Should().BeEquivalentTo(originalProfile);
        (await dbContext.RelationshipTypes.AsNoTracking()
            .Where(type => type.OwnerId == DestinationOwner)
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToArrayAsync()).Should().Equal(originalTypes);
    }

    [Fact]
    public async Task Restore_rejects_an_account_with_existing_content_without_replacing_it()
    {
        var clock = new FakeTimeProvider(Start);
        await using var dbContext = CreateDbContext(clock);
        await SeedRegistrationOnlyAccountAsync(dbContext, DestinationOwner);
        dbContext.People.Add(new Person { OwnerId = DestinationOwner, FirstName = "Existing" });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var act = () => CreateService(dbContext, DestinationOwner, clock).RestoreAsync(ValidEmptyDocument());

        (await act.Should().ThrowAsync<UserDataPortabilityException>())
            .Which.Errors.Should().Contain(UserDataPortabilityError.DestinationNotFresh);
        (await dbContext.People.AsNoTracking()
            .SingleAsync(person => person.OwnerId == DestinationOwner)).FirstName.Should().Be("Existing");
        (await dbContext.RelationshipTypes.AsNoTracking()
            .CountAsync(type => type.OwnerId == DestinationOwner)).Should().Be(RelationshipType.DefaultNames.Count);
    }

    [Fact]
    public async Task Calls_without_an_authenticated_user_throw_before_validation_or_database_access()
    {
        var clock = new FakeTimeProvider(Start);
        await using var dbContext = CreateDbContext(clock);
        var service = CreateService(dbContext, userId: null, clock);

        var export = () => service.ExportAsync();
        var vCard = () => service.ExportPeopleVCardAsync();
        var restore = () => service.RestoreAsync(null!);
        await export.Should().ThrowAsync<UnauthenticatedUserException>();
        await vCard.Should().ThrowAsync<UnauthenticatedUserException>();
        await restore.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    private static async Task<SeededGraph> SeedGraphAsync(
        RelioDbContext dbContext,
        FakeTimeProvider clock,
        string ownerId)
    {
        var createdAtUtc = clock.GetUtcNow().UtcDateTime;
        var cohortStart = DateOnly.FromDateTime(createdAtUtc);
        var profile = new UserProfile
        {
            OwnerId = ownerId,
            TimeZoneId = "Pacific/Kiritimati",
            DisplayName = "Source profile",
            OnboardingDismissed = false,
            BirthdayRemindersEnabled = false,
            DefaultBirthdayLeadDays = 4,
            ReminderEmailDelivery = ReminderEmailDelivery.Immediate,
            UnsubscribeToken = ownerId + "-unsubscribe",
        };
        var relationshipType = new RelationshipType { OwnerId = ownerId, Name = "Trusted", SortOrder = 0 };
        var tag = new Tag { OwnerId = ownerId, Name = "Reflective" };
        var active = new Person
        {
            OwnerId = ownerId,
            FirstName = "Ada",
            LastName = "Lovelace",
            Nickname = "Enchantress",
            RelationshipTypeId = relationshipType.Id,
            BirthdayDay = 29,
            BirthdayMonth = 2,
            HowWeMet = "A shared project.",
            Details = "A private profile detail.",
            LastContactedOn = cohortStart.AddDays(58),
            StayInTouchCadenceDays = 30,
            BirthdayReminderDisabled = true,
            BirthdayReminderLeadDays = 5,
        };
        active.Tags.Add(tag);
        active.ContactMethods.Add(NewContact(ownerId, ContactMethodKind.Email, "Work", "ada@example.com", 2));
        active.ContactMethods.Add(NewContact(ownerId, ContactMethodKind.Phone, "Mobile", "+1 555 123 4567", 7));
        var archived = new Person
        {
            OwnerId = ownerId,
            FirstName = "Grace",
            LastName = "Hopper",
            IsArchived = true,
            ArchivedAtUtc = null,
        };
        archived.Tags.Add(tag);
        archived.ContactMethods.Add(NewContact(ownerId, ContactMethodKind.Address, "Home", "1 Main Street", 4));
        var interaction = new Interaction
        {
            OwnerId = ownerId,
            OccurredOn = cohortStart.AddDays(50),
            Kind = InteractionKind.Meeting,
            Description = "A private conversation with both people.",
        };
        var participants = new[]
        {
            new InteractionParticipant { OwnerId = ownerId, InteractionId = interaction.Id, PersonId = active.Id },
            new InteractionParticipant { OwnerId = ownerId, InteractionId = interaction.Id, PersonId = archived.Id },
        };
        var note = new Note { OwnerId = ownerId, PersonId = active.Id, Text = "A private note.", IsPinned = true };
        var completedReminder = new Reminder
        {
            OwnerId = ownerId,
            PersonId = active.Id,
            Title = "Follow up after the meeting",
            DueDate = cohortStart.AddDays(14),
            Frequency = ReminderFrequency.Monthly,
            LastDeliveredDate = cohortStart.AddDays(14),
        };
        var snoozedReminder = new Reminder
        {
            OwnerId = ownerId,
            PersonId = archived.Id,
            Title = "Check in later",
            DueDate = cohortStart.AddDays(19),
            Frequency = ReminderFrequency.Once,
            SnoozedUntilDate = cohortStart.AddDays(33),
            LastDeliveredDate = cohortStart.AddDays(40),
        };
        var productActivity = new ProductActivity
        {
            OwnerId = ownerId,
            CohortStartedOnUtc = cohortStart,
            LastActiveOnUtc = cohortStart,
            ReturnedInDays30To59 = false,
            RetentionExpiresAtUtc = createdAtUtc.Date.AddDays(ProductActivityRetentionDays),
        };

        dbContext.UserProfiles.Add(profile);
        dbContext.RelationshipTypes.Add(relationshipType);
        dbContext.Tags.Add(tag);
        dbContext.People.AddRange(active, archived);
        dbContext.Interactions.Add(interaction);
        dbContext.InteractionParticipants.AddRange(participants);
        dbContext.Notes.Add(note);
        dbContext.Reminders.AddRange(completedReminder, snoozedReminder);
        dbContext.Set<ProductActivity>().Add(productActivity);
        await dbContext.SaveChangesAsync();

        clock.Advance(TimeSpan.FromDays(31));
        note.Text = "A private note, edited after it was created.";
        completedReminder.IsCompleted = true;
        completedReminder.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync();

        clock.Advance(TimeSpan.FromDays(14));
        productActivity.LastActiveOnUtc = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        productActivity.ReturnedInDays30To59 = true;
        await dbContext.SaveChangesAsync();

        clock.Advance(TimeSpan.FromDays(15));

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
            active.ContactMethods.Select(contact => contact.Id).Concat(archived.ContactMethods.Select(contact => contact.Id)).ToArray(),
            productActivity.Id);
    }

    private static async Task SeedRegistrationOnlyAccountAsync(RelioDbContext dbContext, string ownerId)
    {
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = ownerId,
            TimeZoneId = "UTC",
            ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
            UnsubscribeToken = "destination-token",
        });
        dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(ownerId));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static ContactMethod NewContact(
        string ownerId,
        ContactMethodKind kind,
        string? label,
        string value,
        int sortOrder)
    {
        var normalizedValue = Relio.Application.People.ContactMethodRules.NormalizeValue(kind, value);
        return new ContactMethod
        {
            OwnerId = ownerId,
            Kind = kind,
            Label = label,
            Value = normalizedValue,
            NormalizedValue = Relio.Application.People.ContactMethodRules.ToNormalizedValue(kind, normalizedValue),
            SortOrder = sortOrder,
        };
    }

    private static UserDataExportDocument ValidEmptyDocument() => new()
    {
        FormatVersion = UserDataExportDocument.CurrentFormatVersion,
        ExportedAtUtc = Start.UtcDateTime,
        Profile = new UserProfileSnapshot
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = Start.UtcDateTime.AddDays(-1),
            UpdatedAtUtc = Start.UtcDateTime,
            TimeZoneId = "UTC",
            DisplayName = null,
            OnboardingDismissed = true,
            BirthdayRemindersEnabled = true,
            DefaultBirthdayLeadDays = 0,
            ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
        },
        RelationshipTypes = [],
        Tags = [],
        People = [],
        Interactions = [],
        Notes = [],
        Reminders = [],
        ProductActivity = null,
    };

    private static PersonSnapshot PersonSnapshotFor(Guid id) => new()
    {
        Id = id,
        CreatedAtUtc = Start.UtcDateTime.AddDays(-1),
        UpdatedAtUtc = Start.UtcDateTime,
        FirstName = "Ada",
        LastName = null,
        Nickname = null,
        RelationshipTypeId = null,
        BirthdayDay = null,
        BirthdayMonth = null,
        BirthdayYear = null,
        HowWeMet = null,
        Details = null,
        IsArchived = false,
        ArchivedAtUtc = null,
        LastContactedOn = null,
        StayInTouchCadenceDays = null,
        BirthdayReminderDisabled = false,
        BirthdayReminderLeadDays = null,
        ContactMethods = [],
        TagIds = [],
    };

    private static RelioDbContext CreateDbContext(FakeTimeProvider clock, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptors)
            .Options;
        return new RelioDbContext(options, clock, FieldProtector);
    }

    private static UserDataPortabilityService CreateService(
        RelioDbContext dbContext,
        string? userId,
        TimeProvider clock) =>
        new(dbContext, new FakeCurrentUser(userId), clock);

    private const int ProductActivityRetentionDays = 90;

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
        Guid ProductActivityId)
    {
        public Guid[] PersonIds => [ActivePersonId, ArchivedPersonId];
    }

    private sealed class CountingSaveChangesInterceptor : SaveChangesInterceptor
    {
        private bool _isCounting;

        public int SaveChangesCount { get; private set; }

        public void StartCounting()
        {
            SaveChangesCount = 0;
            _isCounting = true;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_isCounting)
            {
                SaveChangesCount++;
            }

            return ValueTask.FromResult(result);
        }
    }
}
