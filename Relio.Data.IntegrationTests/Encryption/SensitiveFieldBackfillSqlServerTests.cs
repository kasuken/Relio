using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Encryption;

[Collection(SqlServerCollection.Name)]
public sealed class SensitiveFieldBackfillSqlServerTests : IAsyncLifetime
{
    private readonly SensitiveFieldSqlServerDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [SqlServerFact]
    public async Task Backfill_converts_legacy_sql_rows_and_is_idempotent_across_context_restarts()
    {
        var applicationName = $"Relio.SqlServerBackfill.{Guid.NewGuid():N}";
        using var keyRing = DataProtectionTestHarness.CreateIsolatedKeyRing(applicationName);
        const string howWeMet = "legacy meeting narrative";
        const string details = "legacy person details";
        const string noteText = "legacy note text";
        const string interactionDescription = "legacy interaction description";
        const string reminderTitle = "legacy reminder title";
        const string unsubscribeToken = "legacy-unsubscribe-token";
        const string identityValue = "legacy-authenticator-value";
        const string identityLoginProvider = "[AspNetUserStore]";
        const string identityTokenName = "AuthenticatorKey";
        var ownerId = $"privacy-backfill-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(_database.ConnectionString)
            .Options;

        Guid personId;
        Guid noteId;
        Guid interactionId;
        Guid reminderId;
        Guid profileId;
        string userId;
        await using (var dbContext = new RelioDbContext(options, TimeProvider.System, keyRing.FieldProtector))
        {
            var person = new Person
            {
                OwnerId = ownerId,
                FirstName = "Marta",
                HowWeMet = "initial",
                Details = "initial",
            };
            var note = new Note { OwnerId = ownerId, Person = person, Text = "initial" };
            var interaction = new Interaction
            {
                OwnerId = ownerId,
                OccurredOn = new DateOnly(2025, 1, 1),
                Description = "initial",
            };
            var reminder = new Reminder
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                Title = "initial",
                DueDate = new DateOnly(2025, 1, 2),
            };
            var profile = new UserProfile { OwnerId = ownerId, UnsubscribeToken = "initial-token" };
            var user = new RelioUser
            {
                Id = ownerId,
                UserName = $"{ownerId}@example.com",
                NormalizedUserName = $"{ownerId}@example.com".ToUpperInvariant(),
                Email = $"{ownerId}@example.com",
                NormalizedEmail = $"{ownerId}@example.com".ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            };
            var identityToken = new IdentityUserToken<string>
            {
                UserId = user.Id,
                LoginProvider = "[AspNetUserStore]",
                Name = "AuthenticatorKey",
                Value = "initial",
            };

            dbContext.Users.Add(user);
            dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(ownerId));
            dbContext.AddRange(person, note, interaction, reminder, profile, identityToken);
            await dbContext.SaveChangesAsync();
            personId = person.Id;
            noteId = note.Id;
            interactionId = interaction.Id;
            reminderId = reminder.Id;
            profileId = profile.Id;
            userId = user.Id;
        }

        await using (var legacyWriter = new RelioDbContext(
            options,
            TimeProvider.System,
            keyRing.FieldProtector,
            FieldProtectionMode.LegacyBackfill))
        {
            await legacyWriter.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [People] SET [HowWeMet] = {howWeMet}, [Details] = {details}, [SensitiveDataProtectionVersion] = {FieldProtectionSchema.LegacyVersion} WHERE [Id] = {personId};");
            await legacyWriter.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Notes] SET [Text] = {noteText}, [SensitiveDataProtectionVersion] = {FieldProtectionSchema.LegacyVersion} WHERE [Id] = {noteId};");
            await legacyWriter.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Interactions] SET [Description] = {interactionDescription}, [SensitiveDataProtectionVersion] = {FieldProtectionSchema.LegacyVersion} WHERE [Id] = {interactionId};");
            await legacyWriter.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Reminders] SET [Title] = {reminderTitle}, [SensitiveDataProtectionVersion] = {FieldProtectionSchema.LegacyVersion} WHERE [Id] = {reminderId};");
            await legacyWriter.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [UserProfiles] SET [UnsubscribeToken] = {unsubscribeToken}, [UnsubscribeTokenVerifier] = NULL, [SensitiveDataProtectionVersion] = {FieldProtectionSchema.LegacyVersion} WHERE [Id] = {profileId};");
            await legacyWriter.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [AspNetUserTokens] SET [Value] = {identityValue}, [SensitiveDataProtectionVersion] = {FieldProtectionSchema.LegacyVersion} WHERE [UserId] = {userId} AND [LoginProvider] = {identityLoginProvider} AND [Name] = {identityTokenName};");
        }

        var service = new SensitiveFieldBackfillService(options, TimeProvider.System, keyRing.FieldProtector);
        var result = await service.RunAsync();

        result.Should().Be(new SensitiveFieldBackfillResult(1, 1, 1, 1, 1, 1));

        using var restartedKeyRing = keyRing.Restart();
        var restartedService = new SensitiveFieldBackfillService(
            options,
            TimeProvider.System,
            restartedKeyRing.FieldProtector);
        (await restartedService.RunAsync()).Should().Be(new SensitiveFieldBackfillResult(0, 0, 0, 0, 0, 0));

        using var wrongKeyRing = DataProtectionTestHarness.CreateIsolatedKeyRing(applicationName);
        await using (var wrongReader = new RelioDbContext(options, TimeProvider.System, wrongKeyRing.FieldProtector))
        {
            var read = () => wrongReader.People.AsNoTracking().SingleAsync(person => person.Id == personId);
            await read.Should().ThrowAsync<ProtectedFieldAuthenticationException>();
        }

        var wrongKeyBackfill = new SensitiveFieldBackfillService(
            options,
            TimeProvider.System,
            wrongKeyRing.FieldProtector);
        var wrongKeyRun = () => wrongKeyBackfill.RunAsync();
        await wrongKeyRun.Should().ThrowAsync<SensitiveFieldBackfillException>();

        await using (var readContext = new RelioDbContext(
            options,
            TimeProvider.System,
            restartedKeyRing.FieldProtector))
        {
            (await readContext.People.AsNoTracking().SingleAsync(person => person.Id == personId)).HowWeMet
                .Should().Be(howWeMet);
            (await readContext.People.AsNoTracking().SingleAsync(person => person.Id == personId)).Details
                .Should().Be(details);
            (await readContext.Notes.AsNoTracking().SingleAsync(note => note.Id == noteId)).Text.Should().Be(noteText);
            (await readContext.Interactions.AsNoTracking().SingleAsync(interaction => interaction.Id == interactionId))
                .Description.Should().Be(interactionDescription);
            (await readContext.Reminders.AsNoTracking().SingleAsync(reminder => reminder.Id == reminderId))
                .Title.Should().Be(reminderTitle);
            var profile = await readContext.UserProfiles.AsNoTracking().SingleAsync(profile => profile.Id == profileId);
            profile.UnsubscribeToken.Should().Be(unsubscribeToken);
            profile.UnsubscribeTokenVerifier.Should().Be(UnsubscribeTokenHash.Compute(unsubscribeToken));
            (await readContext.UserTokens.AsNoTracking().SingleAsync(token => token.UserId == userId)).Value
                .Should().Be(identityValue);
        }
    }
}
