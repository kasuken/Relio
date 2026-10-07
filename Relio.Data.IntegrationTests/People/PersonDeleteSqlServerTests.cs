using Microsoft.EntityFrameworkCore;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// What only a real SQL Server can prove for issue #26: deleting a person really removes their
/// contact methods and tag links in one save (and leaves the tags and other people's links alone),
/// every foreign key that references <c>People</c> is <c>ON DELETE CASCADE</c> - the backstop behind
/// <c>PeopleService.RemoveDependentsAsync</c> - and archive/restore round trip through the real
/// <c>datetime2</c> column.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PersonDeleteSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task DeleteAsync_removes_the_person_their_contact_methods_and_tag_links_and_keeps_the_tags()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");
        var otherPersonId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Grace");
        var chessId = await TestDataFactory.CreateTagAsync(dbContext, owner, "Chess");
        var climbingId = await TestDataFactory.CreateTagAsync(dbContext, owner, "Climbing");
        dbContext.ChangeTracker.Clear();
        await TestDataFactory.TagPersonAsync(dbContext, personId, chessId);
        await TestDataFactory.TagPersonAsync(dbContext, personId, climbingId);
        await TestDataFactory.TagPersonAsync(dbContext, otherPersonId, chessId);
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, personId, "ada@example.com");
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, personId, "+44 7700 900123", ContactMethodKind.Phone, 1);
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, otherPersonId, "grace@example.com");

        var deleted = await TestDataFactory.CreateService(dbContext, owner).DeleteAsync(personId);

        deleted.Should().BeTrue();
        await using var verify = fixture.CreateDbContext();
        (await verify.People.AnyAsync(p => p.Id == personId)).Should().BeFalse();
        (await verify.ContactMethods.CountAsync(c => c.PersonId == personId)).Should().Be(0);
        (await CountTagLinksAsync(verify, personId)).Should().Be(0);
        (await verify.Tags.CountAsync(t => t.OwnerId == owner)).Should().Be(2, "deleting a person never deletes their tags");
        (await CountTagLinksAsync(verify, otherPersonId)).Should().Be(1, "another person's link to the same tag stays");
        (await verify.ContactMethods.CountAsync(c => c.PersonId == otherPersonId)).Should().Be(1);
        (await verify.People.AnyAsync(p => p.Id == otherPersonId)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task DeleteAsync_deletes_an_archived_person_and_a_second_delete_finds_nobody()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada", isArchived: true);
        var service = TestDataFactory.CreateService(dbContext, owner);

        (await service.DeleteAsync(personId)).Should().BeTrue();
        (await service.DeleteAsync(personId)).Should().BeFalse();

        await using var verify = fixture.CreateDbContext();
        (await verify.People.AnyAsync(p => p.Id == personId)).Should().BeFalse();
    }

    [SqlServerFact]
    public async Task DeleteAsync_for_another_users_person_returns_false_and_deletes_nothing()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");
        await TestDataFactory.CreateContactMethodAsync(dbContext, ownerA, personId, "alice@example.com");

        var deleted = await TestDataFactory.CreateService(dbContext, ownerB).DeleteAsync(personId);

        deleted.Should().BeFalse();
        await using var verify = fixture.CreateDbContext();
        (await verify.People.AnyAsync(p => p.Id == personId)).Should().BeTrue();
        (await verify.ContactMethods.CountAsync(c => c.PersonId == personId)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task Every_foreign_key_that_references_People_cascades()
    {
        await using var dbContext = fixture.CreateDbContext();

        var foreignKeys = await dbContext.Database.SqlQuery<string>($"""
            SELECT CONVERT(nvarchar(300), fk.name) COLLATE DATABASE_DEFAULT + N':'
                 + CONVERT(nvarchar(60), fk.delete_referential_action_desc) COLLATE DATABASE_DEFAULT AS [Value]
            FROM sys.foreign_keys fk
            WHERE fk.referenced_object_id = OBJECT_ID(N'dbo.People')
            """).ToListAsync();

        foreignKeys.Should().NotBeEmpty();
        foreignKeys.Should().OnlyContain(
            foreignKey => foreignKey.EndsWith(":CASCADE"),
            "a child that does not cascade would block deleting its person on SQL Server (or be orphaned)");
        foreignKeys.Should().Contain("FK_PersonTags_People_PeopleId:CASCADE");
        foreignKeys.Should().Contain("FK_ContactMethods_People_PersonId:CASCADE");
    }

    [SqlServerFact]
    public async Task Archive_and_restore_round_trip_on_SQL_Server()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");
        var service = TestDataFactory.CreateService(dbContext, owner);

        (await service.ArchiveAsync(personId)).Should().BeTrue();
        var archived = await service.GetAsync(personId);
        archived!.IsArchived.Should().BeTrue();
        archived.ArchivedAtUtc.Should().NotBeNull().And.BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        (await service.ListAsync()).Should().BeEmpty();

        (await service.RestoreAsync(personId)).Should().BeTrue();
        var restored = await service.GetAsync(personId);
        restored!.IsArchived.Should().BeFalse();
        restored.ArchivedAtUtc.Should().BeNull();
        (await service.ListAsync()).Should().ContainSingle();
    }

    private static Task<int> CountTagLinksAsync(RelioDbContext dbContext, Guid personId) =>
        dbContext.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [PersonTags] WHERE [PeopleId] = {personId}")
            .SingleAsync();
}
