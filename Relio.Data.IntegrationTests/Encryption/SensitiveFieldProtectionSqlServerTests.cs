using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Encryption;

[Collection(SqlServerCollection.Name)]
public sealed class SensitiveFieldProtectionSqlServerTests : IAsyncLifetime
{
    private readonly SensitiveFieldSqlServerDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [SqlServerFact]
    public async Task Maximum_validated_sensitive_values_round_trip_through_sql_server()
    {
        var ownerId = $"privacy-max-{Guid.NewGuid():N}";
        var howWeMet = MaximumLengthValue(Person.HowWeMetMaxLength);
        var details = MaximumLengthValue(Person.DetailsMaxLength);
        var noteText = MaximumLengthValue(Note.TextMaxLength);
        var description = MaximumLengthValue(Interaction.DescriptionMaxLength);
        var reminderTitle = MaximumLengthValue(Reminder.TitleMaxLength);
        var unsubscribeToken = new string('t', 64);
        Guid personId;
        Guid noteId;
        Guid interactionId;
        Guid reminderId;
        Guid profileId;

        await using (var dbContext = _database.CreateDbContext())
        {
            var user = CreateSyntheticUser(ownerId);
            var person = new Person
            {
                OwnerId = ownerId,
                FirstName = "Marta",
                HowWeMet = howWeMet,
                Details = details,
            };
            var note = new Note { OwnerId = ownerId, Person = person, Text = noteText };
            var interaction = new Interaction
            {
                OwnerId = ownerId,
                OccurredOn = new DateOnly(2025, 1, 1),
                Description = description,
            };
            var reminder = new Reminder
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                Title = reminderTitle,
                DueDate = new DateOnly(2025, 1, 2),
            };
            var profile = new UserProfile { OwnerId = ownerId, UnsubscribeToken = unsubscribeToken };
            dbContext.Users.Add(user);
            dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(ownerId));
            dbContext.AddRange(person, note, interaction, reminder, profile);
            await dbContext.SaveChangesAsync();

            personId = person.Id;
            noteId = note.Id;
            interactionId = interaction.Id;
            reminderId = reminder.Id;
            profileId = profile.Id;
        }

        await using var reader = _database.CreateDbContext();
        (await reader.People.AsNoTracking().SingleAsync(person => person.Id == personId)).HowWeMet.Should().Be(howWeMet);
        (await reader.People.AsNoTracking().SingleAsync(person => person.Id == personId)).Details.Should().Be(details);
        (await reader.Notes.AsNoTracking().SingleAsync(note => note.Id == noteId)).Text.Should().Be(noteText);
        (await reader.Interactions.AsNoTracking().SingleAsync(interaction => interaction.Id == interactionId))
            .Description.Should().Be(description);
        (await reader.Reminders.AsNoTracking().SingleAsync(reminder => reminder.Id == reminderId)).Title.Should().Be(reminderTitle);
        (await reader.UserProfiles.AsNoTracking().SingleAsync(profile => profile.Id == profileId))
            .UnsubscribeToken.Should().Be(unsubscribeToken);
    }

    [SqlServerFact]
    public async Task Raw_database_values_do_not_contain_the_sensitive_plaintext_fields()
    {
        var ownerId = $"privacy-{Guid.NewGuid():N}";
        var identityEmail = $"privacy-{Guid.NewGuid():N}@example.com";
        const string howWeMet = "Private first-meeting story";
        const string details = "Private personal context";
        const string noteText = "Private note text";
        const string interactionDescription = "Private interaction description";
        const string reminderTitle = "Private reminder title";
        const string unsubscribeToken = "synthetic-unsubscribe-bearer-token";
        const string identityValue = "synthetic authenticator and recovery value";

        Guid personId;
        string userId;
        await using (var dbContext = _database.CreateDbContext())
        {
            var user = new RelioUser
            {
                Id = ownerId,
                UserName = identityEmail,
                NormalizedUserName = identityEmail.ToUpperInvariant(),
                Email = identityEmail,
                NormalizedEmail = identityEmail.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            };
            dbContext.Users.Add(user);
            dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(ownerId));
            var person = new Person
            {
                OwnerId = ownerId,
                FirstName = "Marta",
                HowWeMet = howWeMet,
                Details = details,
            };
            personId = person.Id;
            dbContext.AddRange(
                person,
                new Note { OwnerId = ownerId, Person = person, Text = noteText },
                new Interaction
                {
                    OwnerId = ownerId,
                    OccurredOn = new DateOnly(2025, 1, 1),
                    Description = interactionDescription,
                },
                new Reminder
                {
                    OwnerId = ownerId,
                    PersonId = personId,
                    Title = reminderTitle,
                    DueDate = new DateOnly(2025, 1, 2),
                },
                new UserProfile { OwnerId = ownerId, UnsubscribeToken = unsubscribeToken },
                new IdentityUserToken<string>
                {
                    UserId = user.Id,
                    LoginProvider = "[AspNetUserStore]",
                    Name = "AuthenticatorKey",
                    Value = identityValue,
                });
            await dbContext.SaveChangesAsync();
            userId = user.Id;
        }

        await using var rawReader = _database.CreateDbContext();
        var rawHowWeMet = await rawReader.Database.SqlQueryRaw<string>(
                "SELECT [HowWeMet] AS [Value] FROM [People] WHERE [Id] = {0}",
                personId)
            .SingleAsync();
        var rawDetails = await rawReader.Database.SqlQueryRaw<string>(
                "SELECT [Details] AS [Value] FROM [People] WHERE [Id] = {0}",
                personId)
            .SingleAsync();
        var rawNote = await rawReader.Database.SqlQueryRaw<string>(
                "SELECT [Text] AS [Value] FROM [Notes] WHERE [PersonId] = {0}",
                personId)
            .SingleAsync();
        var rawDescription = await rawReader.Database.SqlQueryRaw<string>(
                "SELECT [Description] AS [Value] FROM [Interactions] WHERE [OwnerId] = {0}",
                ownerId)
            .SingleAsync();
        var rawReminder = await rawReader.Database.SqlQueryRaw<string>(
                "SELECT [Title] AS [Value] FROM [Reminders] WHERE [PersonId] = {0}",
                personId)
            .SingleAsync();
        var rawUnsubscribeToken = await rawReader.Database.SqlQueryRaw<string>(
                "SELECT [UnsubscribeToken] AS [Value] FROM [UserProfiles] WHERE [OwnerId] = {0}",
                ownerId)
            .SingleAsync();
        var rawIdentityToken = await rawReader.Database.SqlQueryRaw<string>(
                "SELECT [Value] AS [Value] FROM [AspNetUserTokens] WHERE [UserId] = {0}",
                userId)
            .SingleAsync();

        rawHowWeMet.Should().NotContain(howWeMet);
        rawDetails.Should().NotContain(details);
        rawNote.Should().NotContain(noteText);
        rawDescription.Should().NotContain(interactionDescription);
        rawReminder.Should().NotContain(reminderTitle);
        rawUnsubscribeToken.Should().NotContain(unsubscribeToken);
        rawIdentityToken.Should().NotContain(identityValue);
    }

    [SqlServerFact]
    public async Task Sql_values_survive_key_rotation_and_durable_restart_but_reject_a_wrong_key_ring()
    {
        var applicationName = $"Relio.SqlServerRotation.{Guid.NewGuid():N}";
        using var keyRing = DataProtectionTestHarness.CreateIsolatedKeyRing(applicationName);
        var ownerId = $"privacy-rotation-{Guid.NewGuid():N}";
        const string originalStory = "private story protected before rotation";
        const string rotatedStory = "private story protected after rotation";
        Guid originalPersonId;
        Guid rotatedPersonId;

        await using (var dbContext = _database.CreateDbContext(keyRing.FieldProtector))
        {
            var user = CreateSyntheticUser(ownerId);
            var originalPerson = new Person
            {
                OwnerId = ownerId,
                FirstName = "Marta",
                HowWeMet = originalStory,
            };
            dbContext.Users.Add(user);
            dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(ownerId));
            dbContext.People.Add(originalPerson);
            await dbContext.SaveChangesAsync();
            originalPersonId = originalPerson.Id;
        }

        var now = TimeProvider.System.GetUtcNow();
        keyRing.KeyManager.CreateNewKey(now.AddMinutes(-1), now.AddDays(30));
        using var rotatedKeyRing = keyRing.Restart();

        await using (var dbContext = _database.CreateDbContext(rotatedKeyRing.FieldProtector))
        {
            var rotatedPerson = new Person
            {
                OwnerId = ownerId,
                FirstName = "Chiara",
                HowWeMet = rotatedStory,
            };
            dbContext.People.Add(rotatedPerson);
            await dbContext.SaveChangesAsync();
            rotatedPersonId = rotatedPerson.Id;

            var rawRotatedValue = await dbContext.Database.SqlQueryRaw<string>(
                    "SELECT [HowWeMet] AS [Value] FROM [People] WHERE [Id] = {0}",
                    rotatedPersonId)
                .SingleAsync();
            rawRotatedValue.Should().NotContain(rotatedStory);
        }

        using var restartedKeyRing = rotatedKeyRing.Restart();
        await using (var restartedReader = _database.CreateDbContext(restartedKeyRing.FieldProtector))
        {
            (await restartedReader.People.AsNoTracking().SingleAsync(person => person.Id == originalPersonId))
                .HowWeMet.Should().Be(originalStory);
            (await restartedReader.People.AsNoTracking().SingleAsync(person => person.Id == rotatedPersonId))
                .HowWeMet.Should().Be(rotatedStory);
        }

        using var wrongKeyRing = DataProtectionTestHarness.CreateIsolatedKeyRing(applicationName);
        await using var wrongReader = _database.CreateDbContext(wrongKeyRing.FieldProtector);
        var wrongKeyRead = () => wrongReader.People
            .AsNoTracking()
            .SingleAsync(person => person.Id == originalPersonId);
        await wrongKeyRead.Should().ThrowAsync<ProtectedFieldAuthenticationException>();
    }

    private static string MaximumLengthValue(int maximumLength) =>
        new string('\u0800', maximumLength);

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
}
