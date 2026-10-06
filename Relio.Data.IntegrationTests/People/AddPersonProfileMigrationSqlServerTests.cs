using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// Proves the hand-written <c>AddPersonProfile</c> migration keeps real data: it starts from a
/// database at the previous migration with existing accounts and people, migrates to the latest, and
/// checks the birthdays were carried into the new columns and every existing account got the default
/// relationship types. Then it migrates back down and checks which birthdays survive a downgrade.
/// </summary>
/// <remarks>
/// Each test owns a throwaway database rather than using the shared <see cref="SqlServerDatabaseFixture"/>
/// one, because the shared one is already at the latest migration and must stay there.
/// </remarks>
public sealed class AddPersonProfileMigrationSqlServerTests
{
    private const string MigrationName = "AddPersonProfile";

    [SqlServerFact]
    public async Task Upgrading_keeps_every_birthday_and_gives_every_existing_account_the_default_types()
    {
        await using var database = await OldDatabase.CreateAsync();
        await database.AddAccountAsync("with-people");
        await database.AddPersonAsync("with-people", "Dated", "1992-02-29");
        await database.AddPersonAsync("with-people", "Undated", null);
        await database.AddAccountAsync("without-people");

        await database.MigrateToLatestAsync();

        await using var dbContext = database.CreateDbContext();
        var people = await dbContext.People.AsNoTracking().Where(p => p.OwnerId == "with-people").OrderBy(p => p.FirstName).ToListAsync();
        people.Should().HaveCount(2);
        people[0].FirstName.Should().Be("Dated");
        (people[0].BirthdayYear, people[0].BirthdayMonth, people[0].BirthdayDay).Should().Be((1992, 2, 29));
        people[1].FirstName.Should().Be("Undated");
        (people[1].BirthdayYear, people[1].BirthdayMonth, people[1].BirthdayDay).Should().Be((null, null, null));
        people.Should().OnlyContain(p => p.RelationshipTypeId == null);

        foreach (var ownerId in new[] { "with-people", "without-people" })
        {
            var types = await dbContext.RelationshipTypes.AsNoTracking()
                .Where(t => t.OwnerId == ownerId)
                .OrderBy(t => t.SortOrder)
                .ToListAsync();
            types.Select(t => t.Name).Should().Equal("Family", "Partner", "Friend", "Colleague", "Acquaintance", "Other");
            types.Select(t => t.SortOrder).Should().Equal(0, 1, 2, 3, 4, 5);
            types.Should().OnlyContain(t => t.CreatedAtUtc > DateTime.UtcNow.AddHours(-1) && t.UpdatedAtUtc == t.CreatedAtUtc);
        }

        (await database.ColumnExistsAsync("People", "Birthday")).Should().BeFalse("the old column is dropped");
        (await database.ColumnExistsAsync("People", "BirthdayYear")).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Downgrading_restores_dated_birthdays_and_drops_the_ones_without_a_year()
    {
        await using var database = await OldDatabase.CreateAsync();
        await database.MigrateToLatestAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            dbContext.People.AddRange(
                new Relio.Domain.Person { OwnerId = "owner", FirstName = "Dated", BirthdayYear = 1992, BirthdayMonth = 2, BirthdayDay = 29 },
                new Relio.Domain.Person { OwnerId = "owner", FirstName = "Yearless", BirthdayMonth = 3, BirthdayDay = 14 },
                new Relio.Domain.Person { OwnerId = "owner", FirstName = "None" });
            await dbContext.SaveChangesAsync();
        }

        await database.MigrateToAsync(database.PreviousMigrationId);

        (await database.ColumnExistsAsync("People", "Birthday")).Should().BeTrue("the old column is back");
        (await database.ColumnExistsAsync("People", "BirthdayYear")).Should().BeFalse();
        (await database.TableExistsAsync("RelationshipTypes")).Should().BeFalse();
        var birthdays = await database.ReadBirthdaysAsync();
        birthdays["Dated"].Should().Be(new DateOnly(1992, 2, 29));
        birthdays["Yearless"].Should().BeNull("a birthday without a year has no place in the old date column");
        birthdays["None"].Should().BeNull();
    }

    /// <summary>A throwaway database that starts at the migration before <c>AddPersonProfile</c>.</summary>
    private sealed class OldDatabase : IAsyncDisposable
    {
        private readonly string _connectionString;

        private OldDatabase(string connectionString, string previousMigrationId)
        {
            _connectionString = connectionString;
            PreviousMigrationId = previousMigrationId;
        }

        public string PreviousMigrationId { get; }

        public static async Task<OldDatabase> CreateAsync()
        {
            var builder = new SqlConnectionStringBuilder(SqlServerTestEnvironment.ServerConnectionString)
            {
                InitialCatalog = $"Relio_Migration_{Guid.NewGuid():N}",
            };

            // Only used to read the migration ids: listing them never opens a connection.
            await using var probe = new RelioDbContext(
                new DbContextOptionsBuilder<RelioDbContext>().UseSqlServer(builder.ConnectionString).Options,
                TimeProvider.System);
            var migrations = probe.Database.GetMigrations().ToList();
            var index = migrations.FindIndex(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
            index.Should().BeGreaterThan(0, "AddPersonProfile must exist and have a migration before it");

            var database = new OldDatabase(builder.ConnectionString, migrations[index - 1]);
            await database.MigrateToAsync(database.PreviousMigrationId);
            return database;
        }

        public RelioDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<RelioDbContext>().UseSqlServer(_connectionString).Options,
            TimeProvider.System);

        public async Task MigrateToAsync(string targetMigrationId)
        {
            await using var dbContext = CreateDbContext();
            await dbContext.GetService<IMigrator>().MigrateAsync(targetMigrationId);
        }

        public async Task MigrateToLatestAsync()
        {
            await using var dbContext = CreateDbContext();
            await dbContext.Database.MigrateAsync();
        }

        /// <summary>Adds an account row the way a database at the previous migration holds it.</summary>
        public async Task AddAccountAsync(string ownerId)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await ExecuteAsync(
                connection,
                "INSERT INTO [AspNetUsers] ([Id], [UserName], [AccessFailedCount], [EmailConfirmed], [LockoutEnabled], [PhoneNumberConfirmed], [TwoFactorEnabled], [IsDisabled]) " +
                "VALUES (@id, @id, 0, 0, 1, 0, 0, 0)",
                ("@id", ownerId));
        }

        /// <summary>Adds a person with the old single <c>Birthday</c> date column, or none.</summary>
        public async Task AddPersonAsync(string ownerId, string firstName, string? birthday)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await ExecuteAsync(
                connection,
                "INSERT INTO [People] ([Id], [OwnerId], [FirstName], [Birthday], [IsArchived], [CreatedAtUtc], [UpdatedAtUtc]) " +
                "VALUES (NEWID(), @owner, @name, @birthday, 0, SYSUTCDATETIME(), SYSUTCDATETIME())",
                ("@owner", ownerId),
                ("@name", firstName),
                ("@birthday", birthday is null ? DBNull.Value : DateTime.Parse(birthday, System.Globalization.CultureInfo.InvariantCulture)));
        }

        public Task<bool> ColumnExistsAsync(string table, string column) =>
            ScalarExistsAsync(
                "SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @column",
                ("@table", table),
                ("@column", column));

        public Task<bool> TableExistsAsync(string table) =>
            ScalarExistsAsync("SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @table", ("@table", table));

        public async Task<Dictionary<string, DateOnly?>> ReadBirthdaysAsync()
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT [FirstName], [Birthday] FROM [People]", connection);
            await using var reader = await command.ExecuteReaderAsync();

            var result = new Dictionary<string, DateOnly?>();
            while (await reader.ReadAsync())
            {
                result[reader.GetString(0)] = reader.IsDBNull(1) ? null : DateOnly.FromDateTime(reader.GetDateTime(1));
            }

            return result;
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var dbContext = CreateDbContext();
            await dbContext.Database.EnsureDeletedAsync();
        }

        private async Task<bool> ScalarExistsAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return await command.ExecuteScalarAsync() is not null;
        }

        private static async Task ExecuteAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }
    }
}
