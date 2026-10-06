using Microsoft.EntityFrameworkCore;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.IntegrationTests;

/// <summary>
/// Proves SQL Server-specific behaviour that the EF Core InMemory provider used by
/// <c>Relio.Data.Tests</c> cannot: that <see cref="SqlServerDatabaseFixture"/>'s
/// <c>Database.MigrateAsync()</c> actually produces a queryable schema, and that the unique
/// <c>(OwnerId, Name)</c> index on <see cref="Tag"/> (see
/// <c>Relio.Data.Configurations.TagConfiguration</c>) is enforced by the database itself.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DatabaseSchemaSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Applying_the_real_migrations_creates_queryable_People_and_Tags_tables()
    {
        await using var dbContext = fixture.CreateDbContext();

        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();

        appliedMigrations.Should().NotBeEmpty();
        await dbContext.Invoking(c => c.People.CountAsync()).Should().NotThrowAsync();
        await dbContext.Invoking(c => c.Tags.CountAsync()).Should().NotThrowAsync();
    }

    [SqlServerFact]
    public async Task A_tag_name_is_unique_per_owner()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = TestDataFactory.NewOwnerId();
        await TestDataFactory.CreateTagAsync(dbContext, ownerId, "family");

        dbContext.Tags.Add(new Tag { OwnerId = ownerId, Name = "family" });
        var act = () => dbContext.SaveChangesAsync();

        // The unique (OwnerId, Name) index is enforced by SQL Server itself, not by application
        // code - EF Core InMemory does not honour unique indexes at all, so this can only be
        // proven here.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task Different_owners_can_each_have_a_tag_with_the_same_name()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        await TestDataFactory.CreateTagAsync(dbContext, ownerA, "family");

        dbContext.Tags.Add(new Tag { OwnerId = ownerB, Name = "family" });
        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [SqlServerFact]
    public async Task Applying_the_real_migrations_adds_the_PendingEmail_column()
    {
        await using var dbContext = fixture.CreateDbContext();
        var id = Guid.NewGuid().ToString();
        dbContext.Users.Add(new RelioUser
        {
            Id = id,
            UserName = $"{id}@example.com",
            NormalizedUserName = $"{id}@EXAMPLE.COM",
            Email = $"{id}@example.com",
            NormalizedEmail = $"{id}@EXAMPLE.COM",
            PendingEmail = "pending@example.com",
        });
        await dbContext.SaveChangesAsync();

        await using var readContext = fixture.CreateDbContext();
        var pending = await readContext.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.PendingEmail)
            .SingleAsync();

        pending.Should().Be("pending@example.com");
    }
}
