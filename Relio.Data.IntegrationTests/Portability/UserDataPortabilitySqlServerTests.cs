using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Application.Metrics;
using Relio.Application.Portability;
using Relio.Application.Security;
using Relio.Domain;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Identity;
using Relio.Data.Portability;

namespace Relio.Data.IntegrationTests.Portability;

/// <summary>
/// Proves data portability against SQL Server and the real migration/model, including transparent
/// protection of narrative fields and atomic rollback when an insert fails during restore.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class UserDataPortabilitySqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Export_then_restore_round_trips_encrypted_fields_and_remaps_the_graph_in_one_save()
    {
        var sourceOwner = TestDataFactory.NewOwnerId();
        var destinationOwner = TestDataFactory.NewOwnerId();
        var seeded = await SeedSourceAsync(sourceOwner);
        await SeedRegistrationOnlyDestinationAsync(destinationOwner);
        var document = await ExportAsync(sourceOwner);
        var saves = new CountSavesInterceptor();

        await using (var restore = CreateOneStatementPerCommandContext(saves))
        {
            var result = await CreateService(restore, destinationOwner).RestoreAsync(document);

            result.Should().Be(new UserDataRestoreResult(1, 1, 1, 1, 1, 1));
            saves.Saved.Should().Be(1, "the full restore, including profile and relationship types, is one save");
            restore.ChangeTracker.Entries().Should().BeEmpty();
        }

        await using var verify = fixture.CreateDbContext();
        var person = await verify.People.AsNoTracking()
            .Include(item => item.ContactMethods)
            .Include(item => item.Tags)
            .SingleAsync(item => item.OwnerId == destinationOwner);
        person.Id.Should().NotBe(seeded.PersonId);
        person.Details.Should().Be("SQL Server protected private details.");
        person.HowWeMet.Should().Be("SQL Server protected how we met.");
        person.BirthdayDay.Should().Be(29);
        person.BirthdayMonth.Should().Be(2);
        person.BirthdayYear.Should().BeNull();
        person.ContactMethods.Single().Value.Should().Be("ada@example.com");
        person.ContactMethods.Single().NormalizedValue.Should().Be("ada@example.com");
        person.Tags.Should().ContainSingle().Which.Name.Should().Be("Trusted");
        person.Tags.Select(tag => tag.Id).Should().NotContain(seeded.TagId);

        var restoredInteraction = await verify.Interactions.AsNoTracking()
            .SingleAsync(item => item.OwnerId == destinationOwner);
        restoredInteraction.Id.Should().NotBe(seeded.InteractionId);
        restoredInteraction.Description.Should().Be("SQL Server protected interaction narrative.");
        var participant = await verify.InteractionParticipants.AsNoTracking()
            .SingleAsync(item => item.OwnerId == destinationOwner);
        participant.InteractionId.Should().Be(restoredInteraction.Id);
        participant.PersonId.Should().Be(person.Id);
        participant.Id.Should().NotBe(seeded.ParticipantId);

        var note = await verify.Notes.AsNoTracking().SingleAsync(item => item.OwnerId == destinationOwner);
        note.Text.Should().Be("SQL Server protected note narrative.");
        note.PersonId.Should().Be(person.Id);
        note.Id.Should().NotBe(seeded.NoteId);

        var reminder = await verify.Reminders.AsNoTracking().SingleAsync(item => item.OwnerId == destinationOwner);
        reminder.Title.Should().Be("SQL Server protected reminder title.");
        reminder.PersonId.Should().Be(person.Id);
        reminder.IsCompleted.Should().BeTrue();
        reminder.Frequency.Should().Be(ReminderFrequency.Monthly);
        reminder.Id.Should().NotBe(seeded.ReminderId);

        var profile = await verify.UserProfiles.AsNoTracking().SingleAsync(item => item.OwnerId == destinationOwner);
        profile.DisplayName.Should().Be("SQL Source");
        profile.TimeZoneId.Should().Be("Pacific/Kiritimati");
        profile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
        profile.UnsubscribeToken.Should().NotBeNullOrWhiteSpace().And.NotBe("source-unsubscribe-secret");
        profile.Id.Should().NotBe(seeded.ProfileId);

        (await verify.RelationshipTypes.AsNoTracking().Where(item => item.OwnerId == destinationOwner)
            .Select(item => item.Name).ToArrayAsync())
            .Should().Equal(["Confidant"], "the source's removed defaults stay removed");
        (await verify.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(item => item.OwnerId == destinationOwner))
            .Should().BeFalse("product activity is included in the export but never imported");
    }

    [SqlServerFact]
    public async Task Profile_only_restore_with_no_destination_relationship_types_uses_an_implicit_transaction()
    {
        var sourceOwner = TestDataFactory.NewOwnerId();
        var destinationOwner = TestDataFactory.NewOwnerId();
        await SeedRegistrationOnlyDestinationAsync(sourceOwner);
        await SeedRegistrationOnlyDestinationAsync(destinationOwner);

        await using (var sourceSetup = fixture.CreateDbContext())
        {
            var sourceTypes = await sourceSetup.RelationshipTypes
                .Where(item => item.OwnerId == sourceOwner)
                .ToListAsync();
            sourceSetup.RelationshipTypes.RemoveRange(sourceTypes);
            var sourceProfile = await sourceSetup.UserProfiles
                .SingleAsync(item => item.OwnerId == sourceOwner);
            sourceProfile.DisplayName = "Profile-only source";
            sourceProfile.TimeZoneId = "America/New_York";
            await sourceSetup.SaveChangesAsync();
        }

        await using (var destinationSetup = fixture.CreateDbContext())
        {
            var destinationTypes = await destinationSetup.RelationshipTypes
                .Where(item => item.OwnerId == destinationOwner)
                .ToListAsync();
            destinationSetup.RelationshipTypes.RemoveRange(destinationTypes);
            await destinationSetup.SaveChangesAsync();
        }

        var document = await ExportAsync(sourceOwner);
        document.People.Should().BeEmpty();
        document.Tags.Should().BeEmpty();
        document.Interactions.Should().BeEmpty();
        document.Notes.Should().BeEmpty();
        document.Reminders.Should().BeEmpty();
        document.RelationshipTypes.Should().BeEmpty();
        var saves = new CountSavesInterceptor();

        await using (var restore = CreateDefaultBatchingContext(saves))
        {
            var result = await CreateService(restore, destinationOwner).RestoreAsync(document);

            result.Should().Be(new UserDataRestoreResult(0, 0, 0, 0, 0, 0));
            saves.Saved.Should().Be(1, "the profile-only restore is one save");
            restore.ChangeTracker.Entries().Should().BeEmpty();
            restore.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.WhenNeeded);
        }

        await using var verify = fixture.CreateDbContext();
        var restoredProfile = await verify.UserProfiles.AsNoTracking()
            .SingleAsync(item => item.OwnerId == destinationOwner);
        restoredProfile.DisplayName.Should().Be("Profile-only source");
        restoredProfile.TimeZoneId.Should().Be("America/New_York");
        restoredProfile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
        (await verify.RelationshipTypes.AsNoTracking()
            .AnyAsync(item => item.OwnerId == destinationOwner)).Should().BeFalse();
    }

    [SqlServerFact]
    public async Task A_failed_restore_rolls_back_all_rows_and_profile_updates()
    {
        var sourceOwner = TestDataFactory.NewOwnerId();
        var destinationOwner = TestDataFactory.NewOwnerId();
        await SeedSourceAsync(sourceOwner);
        await SeedRegistrationOnlyDestinationAsync(destinationOwner);
        var document = await ExportAsync(sourceOwner);
        var failure = new FailOnNoteInsertInterceptor();
        var saves = new CountSavesInterceptor();

        await using (var restore = CreateOneStatementPerCommandContext(failure, saves))
        {
            var act = () => CreateService(restore, destinationOwner).RestoreAsync(document);

            var exception = await act.Should().ThrowAsync<Exception>();
            exception.Which.ToString().Should().Contain("simulated restore failure");
            failure.WritesBeforeFailure.Should().BeGreaterThan(0, "earlier inserts ran inside the transaction");
            restore.ChangeTracker.Entries().Should().BeEmpty();
            saves.Saved.Should().Be(0, "the failed SaveChanges did not complete");
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.People.CountAsync(item => item.OwnerId == destinationOwner)).Should().Be(0);
        (await verify.Tags.CountAsync(item => item.OwnerId == destinationOwner)).Should().Be(0);
        (await verify.Interactions.CountAsync(item => item.OwnerId == destinationOwner)).Should().Be(0);
        (await verify.Notes.CountAsync(item => item.OwnerId == destinationOwner)).Should().Be(0);
        (await verify.Reminders.CountAsync(item => item.OwnerId == destinationOwner)).Should().Be(0);
        (await verify.RelationshipTypes.AsNoTracking()
            .Where(item => item.OwnerId == destinationOwner)
            .OrderBy(item => item.SortOrder)
            .Select(item => item.Name)
            .ToArrayAsync())
            .Should().Equal(RelationshipType.DefaultNames);
        var profile = await verify.UserProfiles.AsNoTracking().SingleAsync(item => item.OwnerId == destinationOwner);
        profile.TimeZoneId.Should().Be("UTC");
        profile.DisplayName.Should().BeNull();
        profile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.DailyDigest);
        profile.UnsubscribeToken.Should().BeNull();
    }

    [SqlServerFact]
    public async Task Concurrent_restores_of_one_fresh_account_commit_exactly_one_complete_graph()
    {
        var sourceOwner = TestDataFactory.NewOwnerId();
        var destinationOwner = TestDataFactory.NewOwnerId();
        await SeedSourceAsync(sourceOwner);
        await SeedRegistrationOnlyDestinationAsync(destinationOwner);
        var document = await ExportAsync(sourceOwner);
        var saveBarrier = new RestoreSaveBarrierInterceptor();

        await using var firstContext = CreateOneStatementPerCommandContext(saveBarrier);
        await using var secondContext = CreateOneStatementPerCommandContext(saveBarrier);
        var firstAttempt = CaptureRestoreAsync(CreateService(firstContext, destinationOwner).RestoreAsync(document));
        var secondAttempt = CaptureRestoreAsync(CreateService(secondContext, destinationOwner).RestoreAsync(document));

        var attempts = await Task.WhenAll(firstAttempt, secondAttempt);
        attempts.Count(attempt => attempt.Result is not null).Should().Be(1);
        attempts.Single(attempt => attempt.Errors is not null)
            .Errors!.Should().ContainSingle().Which.Should().Be(UserDataPortabilityError.DestinationNotFresh);

        await using var verify = fixture.CreateDbContext();
        var restoredPerson = await verify.People.AsNoTracking()
            .SingleAsync(item => item.OwnerId == destinationOwner);
        restoredPerson.Id.Should().NotBe(document.People.Single().Id);
        (await verify.ContactMethods.CountAsync(item => item.OwnerId == destinationOwner)).Should().Be(1);
        var restoredInteraction = await verify.Interactions.AsNoTracking()
            .SingleAsync(item => item.OwnerId == destinationOwner);
        (await verify.InteractionParticipants.AsNoTracking()
            .CountAsync(item => item.OwnerId == destinationOwner
                && item.InteractionId == restoredInteraction.Id
                && item.PersonId == restoredPerson.Id)).Should().Be(1);
        (await verify.Notes.CountAsync(item => item.OwnerId == destinationOwner
            && item.PersonId == restoredPerson.Id)).Should().Be(1);
        (await verify.Reminders.CountAsync(item => item.OwnerId == destinationOwner
            && item.PersonId == restoredPerson.Id)).Should().Be(1);
        (await verify.Tags.CountAsync(item => item.OwnerId == destinationOwner)).Should().Be(1);
        (await verify.RelationshipTypes.AsNoTracking()
            .Where(item => item.OwnerId == destinationOwner)
            .Select(item => item.Name)
            .ToArrayAsync()).Should().Equal("Confidant");
        (await verify.UserProfiles.CountAsync(item => item.OwnerId == destinationOwner)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task Another_owners_restore_and_account_lifecycle_lock_do_not_block_a_restore()
    {
        var sourceOwner = TestDataFactory.NewOwnerId();
        var blockedOwner = TestDataFactory.NewOwnerId();
        var destinationOwner = TestDataFactory.NewOwnerId();
        await SeedSourceAsync(sourceOwner);
        await SeedRegistrationOnlyDestinationAsync(blockedOwner);
        await SeedRegistrationOnlyDestinationAsync(destinationOwner);
        var document = await ExportAsync(sourceOwner);

        await using var lockingConnection = new SqlConnection(fixture.ConnectionString);
        await lockingConnection.OpenAsync();
        await using var lockingTransaction = (SqlTransaction)await lockingConnection.BeginTransactionAsync();
        await using var command = lockingConnection.CreateCommand();
        command.Transaction = lockingTransaction;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @ownerResource, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 0;
            IF @result < 0 THROW 51000, 'Could not acquire the test restore lock.', 1;
            EXEC @result = sys.sp_getapplock
                @Resource = N'Relio.AccountLifecycle', @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 0;
            IF @result < 0 THROW 51000, 'Could not acquire the test lifecycle lock.', 1;
            """;
        command.Parameters.AddWithValue(
            "@ownerResource",
            "Relio.UserDataRestore.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(blockedOwner))));
        await command.ExecuteNonQueryAsync();

        await using var restore = CreateDefaultBatchingContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await CreateService(restore, destinationOwner).RestoreAsync(document, timeout.Token);

        result.People.Should().Be(1);
        (await restore.People.AsNoTracking()
            .CountAsync(person => person.OwnerId == destinationOwner)).Should().Be(1);
        await lockingTransaction.RollbackAsync();
    }

    private async Task<SeededSource> SeedSourceAsync(string ownerId)
    {
        await using var db = fixture.CreateDbContext();
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var profile = new UserProfile
        {
            OwnerId = ownerId,
            TimeZoneId = "Pacific/Kiritimati",
            DisplayName = "SQL Source",
            BirthdayRemindersEnabled = false,
            DefaultBirthdayLeadDays = 5,
            ReminderEmailDelivery = ReminderEmailDelivery.Immediate,
            UnsubscribeToken = "source-unsubscribe-secret",
        };
        db.Users.Add(CreateIdentityUser(ownerId));
        db.UserProfiles.Add(profile);
        var defaultRelationshipTypes = RelationshipType.CreateDefaults(ownerId);
        db.RelationshipTypes.AddRange(defaultRelationshipTypes);
        await db.SaveChangesAsync();

        // Model a source account that deliberately removed its registration defaults.
        db.RelationshipTypes.RemoveRange(defaultRelationshipTypes);
        var relationshipType = new RelationshipType { OwnerId = ownerId, Name = "Confidant", SortOrder = 0 };
        var tag = new Tag { OwnerId = ownerId, Name = "Trusted" };
        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = "Ada",
            LastName = "Lovelace",
            RelationshipTypeId = relationshipType.Id,
            BirthdayDay = 29,
            BirthdayMonth = 2,
            HowWeMet = "SQL Server protected how we met.",
            Details = "SQL Server protected private details.",
            LastContactedOn = today.AddDays(-1),
            StayInTouchCadenceDays = 30,
            BirthdayReminderDisabled = true,
            BirthdayReminderLeadDays = 4,
        };
        person.Tags.Add(tag);
        person.ContactMethods.Add(new ContactMethod
        {
            OwnerId = ownerId,
            PersonId = person.Id,
            Kind = ContactMethodKind.Email,
            Label = "Work",
            Value = "ada@example.com",
            NormalizedValue = "ada@example.com",
            SortOrder = 0,
        });
        var interaction = new Interaction
        {
            OwnerId = ownerId,
            OccurredOn = today.AddDays(-2),
            Kind = InteractionKind.Meeting,
            Description = "SQL Server protected interaction narrative.",
        };
        var participant = new InteractionParticipant
        {
            OwnerId = ownerId,
            InteractionId = interaction.Id,
            PersonId = person.Id,
        };
        var note = new Note
        {
            OwnerId = ownerId,
            PersonId = person.Id,
            Text = "SQL Server protected note narrative.",
            IsPinned = true,
        };
        var reminder = new Reminder
        {
            OwnerId = ownerId,
            PersonId = person.Id,
            Title = "SQL Server protected reminder title.",
            DueDate = today.AddDays(-30),
            Frequency = ReminderFrequency.Monthly,
            LastDeliveredDate = today.AddDays(-30),
        };
        var cohortStart = today.AddDays(-65);
        var activity = new ProductActivity
        {
            OwnerId = ownerId,
            CohortStartedOnUtc = cohortStart,
            LastActiveOnUtc = cohortStart.AddDays(40),
            ReturnedInDays30To59 = true,
            RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(cohortStart),
        };

        db.RelationshipTypes.Add(relationshipType);
        db.Tags.Add(tag);
        db.People.Add(person);
        db.Interactions.Add(interaction);
        db.InteractionParticipants.Add(participant);
        db.Notes.Add(note);
        db.Reminders.Add(reminder);
        db.Set<ProductActivity>().Add(activity);
        await db.SaveChangesAsync();
        reminder.IsCompleted = true;
        reminder.CompletedAtUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();

        return new SeededSource(
            profile.Id,
            relationshipType.Id,
            tag.Id,
            person.Id,
            interaction.Id,
            participant.Id,
            note.Id,
            reminder.Id);
    }

    private async Task SeedRegistrationOnlyDestinationAsync(string ownerId)
    {
        await using var db = fixture.CreateDbContext();
        db.Users.Add(CreateIdentityUser(ownerId));
        db.UserProfiles.Add(new UserProfile
        {
            OwnerId = ownerId,
            TimeZoneId = "UTC",
            ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
        });
        db.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(ownerId));
        await db.SaveChangesAsync();
    }

    private static RelioUser CreateIdentityUser(string userId)
    {
        var email = $"portability-{Guid.NewGuid():N}@example.com";
        var normalizedEmail = email.ToUpperInvariant();

        return new RelioUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = normalizedEmail,
            Email = email,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        };
    }

    private async Task<UserDataExportDocument> ExportAsync(string ownerId)
    {
        await using var db = fixture.CreateDbContext();
        return await CreateService(db, ownerId).ExportAsync();
    }

    private static UserDataPortabilityService CreateService(RelioDbContext db, string ownerId) =>
        new(db, new FixedCurrentUser(ownerId), TimeProvider.System);

    private RelioDbContext CreateOneStatementPerCommandContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(fixture.ConnectionString, sqlServer => sqlServer.MaxBatchSize(1))
            .AddInterceptors(new UserDataPortabilityRestoreConcurrencyInterceptor())
            .AddInterceptors(interceptors)
            .Options;
        return new RelioDbContext(options, TimeProvider.System, DataProtectionTestHarness.FieldProtector);
    }

    private RelioDbContext CreateDefaultBatchingContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(new UserDataPortabilityRestoreConcurrencyInterceptor())
            .AddInterceptors(interceptors)
            .Options;
        return new RelioDbContext(options, TimeProvider.System, DataProtectionTestHarness.FieldProtector);
    }

    private static async Task<RestoreAttempt> CaptureRestoreAsync(Task<UserDataRestoreResult> restore)
    {
        try
        {
            return new RestoreAttempt(await restore, null);
        }
        catch (UserDataPortabilityException exception)
        {
            return new RestoreAttempt(null, exception.Errors);
        }
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
    }

    private sealed class FailOnNoteInsertInterceptor : DbCommandInterceptor
    {
        public int WritesBeforeFailure { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Check(DbCommand command)
        {
            if (!command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal)
                || !command.CommandText.Contains("[Notes]", StringComparison.Ordinal))
            {
                if (command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal)
                    || command.CommandText.Contains("DELETE FROM", StringComparison.Ordinal)
                    || command.CommandText.Contains("UPDATE [UserProfiles]", StringComparison.Ordinal))
                {
                    WritesBeforeFailure++;
                }

                return;
            }

            throw new InvalidOperationException("simulated restore failure");
        }
    }

    private sealed record SeededSource(
        Guid ProfileId,
        Guid RelationshipTypeId,
        Guid TagId,
        Guid PersonId,
        Guid InteractionId,
        Guid ParticipantId,
        Guid NoteId,
        Guid ReminderId);

    private sealed record RestoreAttempt(
        UserDataRestoreResult? Result,
        IReadOnlyList<UserDataPortabilityError>? Errors);

    private sealed class RestoreSaveBarrierInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _bothRestoresAreReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _bothRestoresAreReady.TrySetResult(true);
            }

            await _bothRestoresAreReady.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

}
