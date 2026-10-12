using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>
/// Writes and reads the drill's synthetic data. Shared by the drill phases and by the local
/// backup/restore round-trip test, so the same rows are proven in both.
/// </summary>
internal static class RestoreDrillData
{
    private const string PersonFirstName = "Rita";

    public static DbContextOptions<RelioDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<RelioDbContext>().UseSqlServer(connectionString).Options;

    /// <summary>
    /// Creates one synthetic account with protected content in every protected column, in a single
    /// save (every new child is added explicitly, as the user-scoped data pattern requires), and
    /// proves it round-trips and is ciphertext at rest.
    /// </summary>
    public static async Task<RestoreDrillEvidence> SeedAsync(
        string connectionString,
        IDataProtectionFieldProtector protector,
        RestoreDrillCanary canary)
    {
        var ownerId = canary.OwnerId;
        var options = CreateOptions(connectionString);

        await using (var dbContext = new RelioDbContext(options, TimeProvider.System, protector))
        {
            var user = new RelioUser
            {
                Id = ownerId,
                UserName = canary.Email,
                NormalizedUserName = canary.Email.ToUpperInvariant(),
                Email = canary.Email,
                NormalizedEmail = canary.Email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            };
            var person = new Person
            {
                OwnerId = ownerId,
                FirstName = PersonFirstName,
                LastName = "Restoredrill",
                HowWeMet = canary.HowWeMet,
                Details = canary.PersonDetails,
            };
            var otherPerson = new Person { OwnerId = ownerId, FirstName = "Marco", LastName = "Example" };
            var note = new Note { OwnerId = ownerId, Person = person, Text = canary.NoteText, IsPinned = true };
            var interaction = new Interaction
            {
                OwnerId = ownerId,
                OccurredOn = new DateOnly(2026, 1, 15),
                Kind = InteractionKind.Call,
                Description = canary.InteractionDescription,
            };
            var participant = new InteractionParticipant { OwnerId = ownerId, Interaction = interaction, Person = person };
            var reminder = new Reminder
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                Title = canary.ReminderTitle,
                DueDate = new DateOnly(2026, 2, 1),
            };
            var difficultMoment = new DifficultMoment
            {
                OwnerId = ownerId,
                Person = person,
                OccurredOn = new DateOnly(2026, 1, 20),
                Description = canary.DifficultMomentDescription,
                Trigger = canary.DifficultMomentTrigger,
                Resolution = canary.DifficultMomentResolution,
                LessonsLearned = canary.DifficultMomentLessonsLearned,
            };
            var profile = new UserProfile { OwnerId = ownerId, UnsubscribeToken = canary.UnsubscribeToken };
            var identityToken = new IdentityUserToken<string>
            {
                UserId = ownerId,
                LoginProvider = "[AspNetUserStore]",
                Name = "AuthenticatorKey",
                Value = canary.IdentityTokenValue,
            };

            dbContext.Users.Add(user);
            dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(ownerId));
            dbContext.AddRange(
                person,
                otherPerson,
                note,
                interaction,
                participant,
                reminder,
                difficultMoment,
                profile,
                identityToken);
            await dbContext.SaveChangesAsync();
        }

        var verified = await VerifyCanaryAsync(connectionString, protector, canary);
        (verified == canary.ProtectedValues.Count).Should().BeTrue(
            "every protected column must read back what was written before the backup is taken");

        var rawValues = await ReadRawProtectedColumnsAsync(connectionString, ownerId);
        rawValues.Should().NotBeEmpty("the seed rows have protected columns");
        rawValues.Count(value => value.Contains(canary.Canary, StringComparison.Ordinal)).Should().Be(
            0,
            "protected columns must be ciphertext at rest, never the plaintext canary");

        return new RestoreDrillEvidence
        {
            SeededUtc = DateTimeOffset.UtcNow,
            LatestMigrationId = await ReadLatestMigrationAsync(connectionString),
            RowCounts = await CountRowsAsync(connectionString, protector),
        };
    }

    /// <summary>
    /// Reads every canary value back through the given key ring and returns how many equal what was
    /// seeded. Values are never put in a message: the result is a number.
    /// </summary>
    public static async Task<int> VerifyCanaryAsync(
        string connectionString,
        IDataProtectionFieldProtector protector,
        RestoreDrillCanary canary)
    {
        var ownerId = canary.OwnerId;
        await using var dbContext = new RelioDbContext(CreateOptions(connectionString), TimeProvider.System, protector);

        var person = await dbContext.People.AsNoTracking()
            .SingleAsync(p => p.OwnerId == ownerId && p.FirstName == PersonFirstName);
        var note = await dbContext.Notes.AsNoTracking().SingleAsync(n => n.OwnerId == ownerId);
        var interaction = await dbContext.Interactions.AsNoTracking().SingleAsync(i => i.OwnerId == ownerId);
        var reminder = await dbContext.Reminders.AsNoTracking().SingleAsync(r => r.OwnerId == ownerId);
        var moment = await dbContext.DifficultMoments.AsNoTracking().SingleAsync(m => m.OwnerId == ownerId);
        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == ownerId);
        var token = await dbContext.UserTokens.AsNoTracking().SingleAsync(t => t.UserId == ownerId);

        (string? Actual, string Expected)[] pairs =
        [
            (person.HowWeMet, canary.HowWeMet),
            (person.Details, canary.PersonDetails),
            (note.Text, canary.NoteText),
            (interaction.Description, canary.InteractionDescription),
            (reminder.Title, canary.ReminderTitle),
            (moment.Description, canary.DifficultMomentDescription),
            (moment.Trigger, canary.DifficultMomentTrigger),
            (moment.Resolution, canary.DifficultMomentResolution),
            (moment.LessonsLearned, canary.DifficultMomentLessonsLearned),
            (profile.UnsubscribeToken, canary.UnsubscribeToken),
            (token.Value, canary.IdentityTokenValue),
        ];
        return pairs.Count(pair => string.Equals(pair.Actual, pair.Expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// Row counts per table. A count never decrypts anything, so it works even when the key ring is
    /// the wrong one.
    /// </summary>
    public static async Task<Dictionary<string, int>> CountRowsAsync(
        string connectionString,
        IDataProtectionFieldProtector protector)
    {
        await using var dbContext = new RelioDbContext(CreateOptions(connectionString), TimeProvider.System, protector);
        return new Dictionary<string, int>
        {
            ["Users"] = await dbContext.Users.CountAsync(),
            ["UserProfiles"] = await dbContext.UserProfiles.CountAsync(),
            ["UserTokens"] = await dbContext.UserTokens.CountAsync(),
            ["RelationshipTypes"] = await dbContext.RelationshipTypes.CountAsync(),
            ["People"] = await dbContext.People.CountAsync(),
            ["Notes"] = await dbContext.Notes.CountAsync(),
            ["Interactions"] = await dbContext.Interactions.CountAsync(),
            ["InteractionParticipants"] = await dbContext.InteractionParticipants.CountAsync(),
            ["Reminders"] = await dbContext.Reminders.CountAsync(),
            ["DifficultMoments"] = await dbContext.DifficultMoments.CountAsync(),
        };
    }

    public static async Task<string> ReadLatestMigrationAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP 1 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC";
        return await command.ExecuteScalarAsync() as string ?? string.Empty;
    }

    /// <summary>The raw stored text of every protected column of the owner's rows (ciphertext).</summary>
    public static async Task<List<string>> ReadRawProtectedColumnsAsync(string connectionString, string ownerId)
    {
        (string Table, string Column, string OwnerColumn)[] columns =
        [
            ("People", "HowWeMet", "OwnerId"),
            ("People", "Details", "OwnerId"),
            ("Notes", "Text", "OwnerId"),
            ("Interactions", "Description", "OwnerId"),
            ("Reminders", "Title", "OwnerId"),
            ("DifficultMoments", "Description", "OwnerId"),
            ("DifficultMoments", "Trigger", "OwnerId"),
            ("DifficultMoments", "Resolution", "OwnerId"),
            ("DifficultMoments", "LessonsLearned", "OwnerId"),
            ("UserProfiles", "UnsubscribeToken", "OwnerId"),
            ("AspNetUserTokens", "Value", "UserId"),
        ];

        var values = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var (table, column, ownerColumn) in columns)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT [{column}] FROM [{table}] WHERE [{ownerColumn}] = @owner";
            command.Parameters.AddWithValue("@owner", ownerId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    values.Add(reader.GetString(0));
                }
            }
        }

        return values;
    }
}
