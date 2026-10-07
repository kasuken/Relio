using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data.Tests.Encryption;

public sealed class SensitiveFieldBackfillTests
{
    [Fact]
    public async Task Backfill_protects_all_legacy_fields_once_and_preserves_marker_like_text()
    {
        using var keyRing = CreateIsolatedKeyRing();
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        var options = CreateOptions(databaseName, root);
        var markerLookingLegacyText = keyRing.FieldProtector.Protect(
            "This was ordinary legacy text.",
            ProtectedFieldPurposes.PersonHowWeMet)!;

        var (personId, noteId, interactionId, reminderId, profileId, userId) =
            await SeedLegacyDataAsync(options, keyRing.FieldProtector);

        await ReplaceWithLegacyPlaintextAsync(
            options,
            keyRing.FieldProtector,
            personId,
            noteId,
            interactionId,
            reminderId,
            profileId,
            userId,
            markerLookingLegacyText);

        var backfill = new SensitiveFieldBackfillService(options, TimeProvider.System, keyRing.FieldProtector);
        var firstRun = await backfill.RunAsync();

        firstRun.Should().Be(new SensitiveFieldBackfillResult(1, 1, 1, 1, 1, 1));
        await using (var backfillModeReader = new RelioDbContext(
            options,
            TimeProvider.System,
            keyRing.FieldProtector,
            FieldProtectionMode.LegacyBackfill))
        {
            var person = await backfillModeReader.People.AsNoTracking().SingleAsync();
            var note = await backfillModeReader.Notes.AsNoTracking().SingleAsync();
            var interaction = await backfillModeReader.Interactions.AsNoTracking().SingleAsync();
            var reminder = await backfillModeReader.Reminders.AsNoTracking().SingleAsync();
            var profile = await backfillModeReader.UserProfiles.AsNoTracking().SingleAsync();
            var token = await backfillModeReader.UserTokens.AsNoTracking().SingleAsync();

            person.HowWeMet.Should().NotBe(markerLookingLegacyText);
            keyRing.FieldProtector.Unprotect(person.HowWeMet, ProtectedFieldPurposes.PersonHowWeMet)
                .Should().Be(markerLookingLegacyText);
            person.Details.Should().NotBeEmpty();
            keyRing.FieldProtector.Unprotect(person.Details, ProtectedFieldPurposes.PersonDetails).Should().BeEmpty();
            keyRing.FieldProtector.Unprotect(note.Text, ProtectedFieldPurposes.NoteText)
                .Should().Be("Legacy note — 東京 🌿");
            interaction.Description.Should().NotBeEmpty();
            keyRing.FieldProtector.Unprotect(
                interaction.Description,
                ProtectedFieldPurposes.InteractionDescription).Should().BeEmpty();
            keyRing.FieldProtector.Unprotect(reminder.Title, ProtectedFieldPurposes.ReminderTitle)
                .Should().Be("Legacy reminder");
            keyRing.FieldProtector.Unprotect(profile.UnsubscribeToken, ProtectedFieldPurposes.UnsubscribeToken)
                .Should().Be("legacy-unsubscribe-token");
            profile.UnsubscribeTokenVerifier.Should().Be(UnsubscribeTokenHash.Compute("legacy-unsubscribe-token"));
            keyRing.FieldProtector.Unprotect(token.Value, ProtectedFieldPurposes.IdentityUserTokenValue)
                .Should().Be("legacy authenticator/recovery payload");
        }

        (await backfill.RunAsync()).Should().Be(new SensitiveFieldBackfillResult(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Backfill_resumes_after_a_committed_batch_without_double_protecting_rows()
    {
        using var keyRing = CreateIsolatedKeyRing();
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        var interceptor = new FailSecondBatchInterceptor();
        var interruptedOptions = CreateOptions(databaseName, root, interceptor);
        var ids = await SeedManyPeopleAsync(interruptedOptions, 101, keyRing.FieldProtector);

        await ReplacePeopleWithLegacyPlaintextAsync(interruptedOptions, keyRing.FieldProtector, ids);
        interceptor.Enabled = true;

        var interruptedBackfill = new SensitiveFieldBackfillService(
            interruptedOptions,
            TimeProvider.System,
            keyRing.FieldProtector);
        var failedRun = () => interruptedBackfill.RunAsync();
        await failedRun.Should().ThrowAsync<SensitiveFieldBackfillException>();

        await using (var backfillModeReader = new RelioDbContext(
            interruptedOptions,
            TimeProvider.System,
            keyRing.FieldProtector,
            FieldProtectionMode.LegacyBackfill))
        {
            var versions = await backfillModeReader.People
                .AsNoTracking()
                .Select(person => EF.Property<int>(person, FieldProtectionSchema.VersionPropertyName))
                .ToListAsync();
            versions.Count(version => version == FieldProtectionSchema.CurrentVersion).Should().Be(100);
            versions.Count(version => version == FieldProtectionSchema.LegacyVersion).Should().Be(1);
        }

        using var wrongKeyRing = CreateIsolatedKeyRing();
        await using (var backfillModeReader = new RelioDbContext(
            interruptedOptions,
            TimeProvider.System,
            keyRing.FieldProtector,
            FieldProtectionMode.LegacyBackfill))
        {
            var protectedPerson = await backfillModeReader.People.AsNoTracking()
                .FirstAsync(person => EF.Property<int>(
                    person,
                    FieldProtectionSchema.VersionPropertyName) == FieldProtectionSchema.CurrentVersion);
            var wrongKeyRead = () => wrongKeyRing.FieldProtector.Unprotect(
                protectedPerson.HowWeMet,
                ProtectedFieldPurposes.PersonHowWeMet);
            wrongKeyRead.Should().Throw<ProtectedFieldAuthenticationException>();
        }

        await using (var backfillModeReader = new RelioDbContext(
            interruptedOptions,
            TimeProvider.System,
            keyRing.FieldProtector,
            FieldProtectionMode.LegacyBackfill))
        {
            var remainingLegacy = await backfillModeReader.People.AsNoTracking()
                .SingleAsync(person => EF.Property<int>(
                    person,
                    FieldProtectionSchema.VersionPropertyName) == FieldProtectionSchema.LegacyVersion);
            remainingLegacy.HowWeMet.Should().Be("legacy private text");
        }

        var restartedOptions = CreateOptions(databaseName, root);
        var restartedBackfill = new SensitiveFieldBackfillService(
            restartedOptions,
            TimeProvider.System,
            keyRing.FieldProtector);
        var resumed = await restartedBackfill.RunAsync();

        resumed.People.Should().Be(1);
        await using var completedReader = new RelioDbContext(
            restartedOptions,
            TimeProvider.System,
            keyRing.FieldProtector,
            FieldProtectionMode.LegacyBackfill);
        var storedValues = await completedReader.People
            .AsNoTracking()
            .OrderBy(person => person.FirstName)
            .Select(person => person.HowWeMet)
            .ToListAsync();
        storedValues.Should().HaveCount(101);
        storedValues
            .Select(value => keyRing.FieldProtector.Unprotect(value, ProtectedFieldPurposes.PersonHowWeMet))
            .Should().OnlyContain(value => value == "legacy private text");
    }

    [Fact]
    public async Task Backfill_refuses_to_report_completion_when_a_versioned_value_is_tampered()
    {
        using var keyRing = CreateIsolatedKeyRing();
        var options = CreateOptions(Guid.NewGuid().ToString("N"), new InMemoryDatabaseRoot());
        var (personId, _, _, _, _, _) = await SeedLegacyDataAsync(options, keyRing.FieldProtector);

        await using (var backfillModeWriter = new RelioDbContext(
            options,
            TimeProvider.System,
            keyRing.FieldProtector,
            FieldProtectionMode.LegacyBackfill))
        {
            var person = await backfillModeWriter.People.SingleAsync(item => item.Id == personId);
            person.HowWeMet = "not authenticated ciphertext";
            person.Details = keyRing.FieldProtector.Protect(person.Details, ProtectedFieldPurposes.PersonDetails);
            SetCurrentVersion(backfillModeWriter, person);
            await backfillModeWriter.SaveChangesAsync();
        }

        var backfill = new SensitiveFieldBackfillService(options, TimeProvider.System, keyRing.FieldProtector);
        var run = () => backfill.RunAsync();
        await run.Should().ThrowAsync<SensitiveFieldBackfillException>();
    }

    private static async Task<(Guid, Guid, Guid, Guid, Guid, string)> SeedLegacyDataAsync(
        DbContextOptions<RelioDbContext> options,
        IDataProtectionFieldProtector protector)
    {
        await using var dbContext = new RelioDbContext(
            options,
            TimeProvider.System,
            protector,
            FieldProtectionMode.LegacyBackfill);
        await dbContext.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid().ToString("N");
        var user = CreateSyntheticUser(userId);
        var person = new Person { OwnerId = userId, FirstName = "Marta", HowWeMet = "Initial story" };
        var note = new Note { OwnerId = userId, Person = person, Text = "Initial note" };
        var interaction = new Interaction
        {
            OwnerId = userId,
            OccurredOn = new DateOnly(2025, 1, 1),
            Description = "Initial interaction",
        };
        var reminder = new Reminder
        {
            OwnerId = userId,
            PersonId = person.Id,
            Title = "Initial reminder",
            DueDate = new DateOnly(2025, 1, 2),
        };
        var profile = new UserProfile
        {
            OwnerId = userId,
            UnsubscribeToken = "initial-unsubscribe-token",
        };
        var userToken = new IdentityUserToken<string>
        {
            UserId = userId,
            LoginProvider = "AspNetUserStore",
            Name = "AuthenticatorKey",
            Value = "initial identity token",
        };

        dbContext.Users.Add(user);
        dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(userId));
        dbContext.AddRange(person, note, interaction, reminder, profile, userToken);
        SetLegacyVersion(dbContext, person);
        SetLegacyVersion(dbContext, note);
        SetLegacyVersion(dbContext, interaction);
        SetLegacyVersion(dbContext, reminder);
        SetLegacyVersion(dbContext, profile);
        SetLegacyVersion(dbContext, userToken);
        await dbContext.SaveChangesAsync();
        return (person.Id, note.Id, interaction.Id, reminder.Id, profile.Id, userId);
    }

    private static async Task ReplaceWithLegacyPlaintextAsync(
        DbContextOptions<RelioDbContext> options,
        IDataProtectionFieldProtector protector,
        Guid personId,
        Guid noteId,
        Guid interactionId,
        Guid reminderId,
        Guid profileId,
        string userId,
        string markerLookingLegacyText)
    {
        await using var dbContext = new RelioDbContext(
            options,
            TimeProvider.System,
            protector,
            FieldProtectionMode.LegacyBackfill);
        var person = await dbContext.People.SingleAsync(item => item.Id == personId);
        person.HowWeMet = markerLookingLegacyText;
        person.Details = string.Empty;
        var note = await dbContext.Notes.SingleAsync(item => item.Id == noteId);
        note.Text = "Legacy note — 東京 🌿";
        var interaction = await dbContext.Interactions.SingleAsync(item => item.Id == interactionId);
        interaction.Description = string.Empty;
        var reminder = await dbContext.Reminders.SingleAsync(item => item.Id == reminderId);
        reminder.Title = "Legacy reminder";
        var profile = await dbContext.UserProfiles.SingleAsync(item => item.Id == profileId);
        profile.UnsubscribeToken = "legacy-unsubscribe-token";
        profile.UnsubscribeTokenVerifier = null;
        var token = await dbContext.UserTokens.SingleAsync(item => item.UserId == userId);
        token.Value = "legacy authenticator/recovery payload";

        SetLegacyVersion(dbContext, person);
        SetLegacyVersion(dbContext, note);
        SetLegacyVersion(dbContext, interaction);
        SetLegacyVersion(dbContext, reminder);
        SetLegacyVersion(dbContext, profile);
        SetLegacyVersion(dbContext, token);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<List<Guid>> SeedManyPeopleAsync(
        DbContextOptions<RelioDbContext> options,
        int count,
        IDataProtectionFieldProtector protector)
    {
        await using var dbContext = new RelioDbContext(
            options,
            TimeProvider.System,
            protector,
            FieldProtectionMode.LegacyBackfill);
        await dbContext.Database.EnsureCreatedAsync();
        var ownerId = Guid.NewGuid().ToString("N");
        var user = CreateSyntheticUser(ownerId);
        dbContext.Users.Add(user);
        dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(ownerId));
        var people = Enumerable.Range(0, count)
            .Select(index => new Person
            {
                OwnerId = ownerId,
                FirstName = $"Person {index:D3}",
                HowWeMet = "Initial story",
            })
            .ToList();
        dbContext.People.AddRange(people);
        foreach (var person in people)
        {
            SetLegacyVersion(dbContext, person);
        }

        await dbContext.SaveChangesAsync();
        return people.Select(person => person.Id).ToList();
    }

    private static RelioUser CreateSyntheticUser(string userId)
    {
        var email = $"{userId}@example.com";
        return new RelioUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        };
    }

    private static async Task ReplacePeopleWithLegacyPlaintextAsync(
        DbContextOptions<RelioDbContext> options,
        IDataProtectionFieldProtector protector,
        IReadOnlyCollection<Guid> ids)
    {
        await using var dbContext = new RelioDbContext(
            options,
            TimeProvider.System,
            protector,
            FieldProtectionMode.LegacyBackfill);
        var people = await dbContext.People.Where(person => ids.Contains(person.Id)).ToListAsync();
        foreach (var person in people)
        {
            person.HowWeMet = "legacy private text";
            SetLegacyVersion(dbContext, person);
        }

        await dbContext.SaveChangesAsync();
    }

    private static void SetLegacyVersion<TEntity>(RelioDbContext dbContext, TEntity entity)
        where TEntity : class =>
        dbContext.Entry(entity)
            .Property<int>(FieldProtectionSchema.VersionPropertyName)
            .CurrentValue = FieldProtectionSchema.LegacyVersion;

    private static void SetCurrentVersion<TEntity>(RelioDbContext dbContext, TEntity entity)
        where TEntity : class =>
        dbContext.Entry(entity)
            .Property<int>(FieldProtectionSchema.VersionPropertyName)
            .CurrentValue = FieldProtectionSchema.CurrentVersion;

    private static DbContextOptions<RelioDbContext> CreateOptions(
        string databaseName,
        InMemoryDatabaseRoot root,
        ISaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(databaseName, root);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return builder.Options;
    }

    private sealed class FailSecondBatchInterceptor : SaveChangesInterceptor
    {
        private int _saveCount;

        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && Interlocked.Increment(ref _saveCount) == 2)
            {
                throw new InvalidOperationException("Synthetic failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
