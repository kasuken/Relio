using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Data.Configurations;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// What only a real SQL Server can prove for issue #129 (links between people): owner isolation, query
/// translation of the either-end filter, the unique pair index and the lost-race translation, the check
/// constraints, and the foreign key that deliberately does not cascade (<c>RelatedPersonId</c>).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PersonLinkSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task The_pair_index_is_unique_and_the_related_end_has_its_own_index()
    {
        await using var dbContext = fixture.CreateDbContext();

        var unique = await IndexColumnsAsync(dbContext, PersonLinkConfiguration.PairIndexName);
        var related = await IndexColumnsAsync(dbContext, "IX_PersonLinks_OwnerId_RelatedPersonId");

        unique.Should().Be("OwnerId,PersonId,RelatedPersonId");
        related.Should().Be("OwnerId,RelatedPersonId");
        (await dbContext.Database.SqlQuery<bool>($"""
            SELECT CAST(i.is_unique AS bit) AS [Value]
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID('dbo.PersonLinks') AND i.name = {PersonLinkConfiguration.PairIndexName}
            """).SingleAsync()).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Person_links_are_isolated_by_owner_for_all_reads_and_mutations()
    {
        var ownerA = await TestDataFactory.CreateOwnerAsync(fixture);
        var ownerB = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid annaA, marcoA, beaB;
        Guid linkA;
        await using (var setup = fixture.CreateDbContext())
        {
            annaA = await TestDataFactory.CreatePersonAsync(setup, ownerA, "Anna");
            marcoA = await TestDataFactory.CreatePersonAsync(setup, ownerA, "Marco");
            beaB = await TestDataFactory.CreatePersonAsync(setup, ownerB, "Bea");
            linkA = await TestDataFactory.CreatePersonLinkService(setup, ownerA)
                .CreateAsync(Request(annaA, marcoA, PersonLinkKind.Friend));
        }

        await using var db = fixture.CreateDbContext();
        var serviceB = TestDataFactory.CreatePersonLinkService(db, ownerB);

        // CrossOwner: another owner's person and link are invisible and untouchable.
        (await serviceB.ListForPersonAsync(annaA)).Should().BeEmpty();
        (await serviceB.UpdateAsync(linkA, new UpdatePersonLinkRequest { PersonId = annaA, Kind = PersonLinkKind.Manager }))
            .Should().BeFalse();
        (await serviceB.DeleteAsync(linkA)).Should().BeFalse();

        // MissingPrimary: a link that never existed answers the same way.
        (await serviceB.UpdateAsync(Guid.NewGuid(), new UpdatePersonLinkRequest { PersonId = beaB, Kind = PersonLinkKind.Friend }))
            .Should().BeFalse();
        (await serviceB.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
        (await serviceB.ListForPersonAsync(Guid.NewGuid())).Should().BeEmpty();

        // ForeignIdRejected: linking user A's people, or one of each, fails the same way as an unknown id.
        var bothForeign = () => serviceB.CreateAsync(Request(annaA, marcoA, PersonLinkKind.Friend));
        var oneForeign = () => serviceB.CreateAsync(Request(beaB, annaA, PersonLinkKind.Friend));
        var oneUnknown = () => serviceB.CreateAsync(Request(beaB, Guid.NewGuid(), PersonLinkKind.Friend));
        var foreignMessage = (await bothForeign.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which.Message;
        (await oneForeign.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which.Message.Should().Be(foreignMessage);
        (await oneUnknown.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which.Message.Should().Be(foreignMessage);

        await using var verify = fixture.CreateDbContext();
        var stored = await verify.PersonLinks.AsNoTracking().Where(l => l.OwnerId == ownerA || l.OwnerId == ownerB).ToListAsync();
        stored.Should().ContainSingle().Which.Kind.Should().Be(PersonLinkKind.Friend, "owner A's link is untouched");
    }

    [SqlServerFact]
    public async Task Person_link_creation_reads_updates_and_deletes_round_trip_on_SQL_Server()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid anna, marco, zoe;
        await using (var setup = fixture.CreateDbContext())
        {
            anna = await TestDataFactory.CreatePersonAsync(setup, owner, "Anna", "Rossi");
            marco = await TestDataFactory.CreatePersonAsync(setup, owner, "Marco", "Rossi");
            zoe = await TestDataFactory.CreatePersonAsync(setup, owner, "Zoe", "Bianchi");
        }

        await using var db = fixture.CreateDbContext();
        var service = TestDataFactory.CreatePersonLinkService(db, owner);
        var parentLink = await service.CreateAsync(Request(anna, marco, PersonLinkKind.Parent));
        await service.CreateAsync(Request(zoe, anna, PersonLinkKind.Colleague));

        var fromAnna = await service.ListForPersonAsync(anna);
        var fromMarco = await service.ListForPersonAsync(marco);

        fromAnna.Select(c => (c.OtherDisplayName, c.Role)).Should().Equal(
            ("Marco Rossi", PersonLinkKind.Parent),
            ("Zoe Bianchi", PersonLinkKind.Colleague));
        fromMarco.Should().ContainSingle().Which.Should().Match<PersonConnection>(
            c => c.OtherPersonId == anna && c.Role == PersonLinkKind.Child && c.LinkId == parentLink);

        (await service.UpdateAsync(parentLink, new UpdatePersonLinkRequest { PersonId = marco, Kind = PersonLinkKind.Manager }))
            .Should().BeTrue();
        (await service.ListForPersonAsync(anna)).Single(c => c.OtherPersonId == marco).Role
            .Should().Be(PersonLinkKind.DirectReport);

        (await service.DeleteAsync(parentLink)).Should().BeTrue();
        (await service.ListForPersonAsync(marco)).Should().BeEmpty();
        (await service.ListForPersonAsync(anna)).Should().ContainSingle();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task Linking_archived_people_is_refused_but_existing_links_stay_listed()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid anna, marco, bruno;
        await using (var setup = fixture.CreateDbContext())
        {
            anna = await TestDataFactory.CreatePersonAsync(setup, owner, "Anna");
            marco = await TestDataFactory.CreatePersonAsync(setup, owner, "Marco", isArchived: true);
            bruno = await TestDataFactory.CreatePersonAsync(setup, owner, "Bruno");
            await TestDataFactory.CreatePersonLinkAsync(setup, owner, anna, marco, PersonLinkKind.Friend);
        }

        await using var db = fixture.CreateDbContext();
        var service = TestDataFactory.CreatePersonLinkService(db, owner);

        var toArchived = () => service.CreateAsync(Request(anna, marco, PersonLinkKind.Colleague));
        var fromArchived = () => service.CreateAsync(Request(marco, bruno, PersonLinkKind.Colleague));
        (await toArchived.Should().ThrowAsync<PersonLinkValidationException>())
            .Which.Errors.Should().Equal(PersonLinkValidationError.ArchivedPersonNotAllowed);
        (await fromArchived.Should().ThrowAsync<PersonLinkValidationException>())
            .Which.Errors.Should().Equal(PersonLinkValidationError.ArchivedPersonNotAllowed);

        var listed = await service.ListForPersonAsync(anna);
        listed.Should().ContainSingle().Which.OtherIsArchived.Should().BeTrue();
        (await service.UpdateAsync(listed[0].LinkId, new UpdatePersonLinkRequest { PersonId = anna, Kind = PersonLinkKind.Relative }))
            .Should().BeTrue();
    }

    [SqlServerFact]
    public async Task The_unique_pair_index_rejects_the_same_pair_twice()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid anna, marco;
        await using (var setup = fixture.CreateDbContext())
        {
            anna = await TestDataFactory.CreatePersonAsync(setup, owner, "Anna");
            marco = await TestDataFactory.CreatePersonAsync(setup, owner, "Marco");
            await TestDataFactory.CreatePersonLinkAsync(setup, owner, anna, marco, PersonLinkKind.Friend);
        }

        await using var db = fixture.CreateDbContext();
        var normalized = new PersonLink { OwnerId = owner };
        normalized.SetEnds(anna, marco, PersonLinkKind.Colleague);
        db.PersonLinks.Add(normalized);

        var act = () => db.SaveChangesAsync();

        var exception = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        exception.InnerException.Should().BeOfType<SqlException>().Which.Message
            .Should().Contain(PersonLinkConfiguration.PairIndexName);
    }

    [SqlServerFact]
    public async Task A_lost_race_for_the_same_pair_becomes_AlreadyLinked()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid anna, marco;
        await using (var setup = fixture.CreateDbContext())
        {
            anna = await TestDataFactory.CreatePersonAsync(setup, owner, "Anna");
            marco = await TestDataFactory.CreatePersonAsync(setup, owner, "Marco");
        }

        // Just before our save, another request links the same pair from the other profile.
        var interceptor = new RunOnFirstSaveInterceptor(async ct =>
        {
            await using var other = fixture.CreateDbContext();
            await TestDataFactory.CreatePersonLinkService(other, owner)
                .CreateAsync(Request(marco, anna, PersonLinkKind.Child), ct);
        });
        await using var dbContext = fixture.CreateDbContext(interceptor);

        var act = () => TestDataFactory.CreatePersonLinkService(dbContext, owner)
            .CreateAsync(Request(anna, marco, PersonLinkKind.Parent));

        var exception = (await act.Should().ThrowAsync<PersonLinkValidationException>()).Which;
        exception.Errors.Should().Equal(PersonLinkValidationError.AlreadyLinked);
        exception.InnerException.Should().BeNull("the database error quotes the owner id");
        interceptor.Fired.Should().BeTrue();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        await using var fresh = fixture.CreateDbContext();
        (await fresh.PersonLinks.CountAsync(l => l.OwnerId == owner)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task Check_constraints_reject_a_self_link_and_an_unknown_kind()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid anna, marco;
        await using (var setup = fixture.CreateDbContext())
        {
            anna = await TestDataFactory.CreatePersonAsync(setup, owner, "Anna");
            marco = await TestDataFactory.CreatePersonAsync(setup, owner, "Marco");
        }

        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        var self = () => InsertRawAsync(db, owner, anna, anna, "Friend", now);
        var unknownKind = () => InsertRawAsync(db, owner, anna, marco, "Rival", now);

        (await self.Should().ThrowAsync<SqlException>()).Which.Message.Should().Contain("CK_PersonLinks_DistinctPeople");
        (await unknownKind.Should().ThrowAsync<SqlException>()).Which.Message.Should().Contain("CK_PersonLinks_Kind");
        (await InsertRawAsync(db, owner, anna, marco, "DirectReport", now)).Should().Be(1, "a valid row passes");
    }

    [SqlServerFact]
    public async Task The_related_person_foreign_key_does_not_cascade_but_the_first_one_does()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid first, second;
        Guid linkId;
        await using (var setup = fixture.CreateDbContext())
        {
            first = await TestDataFactory.CreatePersonAsync(setup, owner, "Anna", id: TestDataFactory.GuidStartingWith(0x10000000));
            second = await TestDataFactory.CreatePersonAsync(setup, owner, "Marco", id: TestDataFactory.GuidStartingWith(0xF0000000));
            linkId = await TestDataFactory.CreatePersonLinkAsync(setup, owner, first, second, PersonLinkKind.Friend);
        }

        await using (var refused = fixture.CreateDbContext())
        {
            // The related (higher) end: the database refuses, so the services must delete the link first.
            var act = () => refused.People.Where(p => p.Id == second).ExecuteDeleteAsync();
            (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
        }

        await using (var cascades = fixture.CreateDbContext())
        {
            (await cascades.People.Where(p => p.Id == first).ExecuteDeleteAsync()).Should().Be(1);
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.PersonLinks.AnyAsync(l => l.Id == linkId)).Should().BeFalse("the first end cascades");
        (await verify.People.AnyAsync(p => p.Id == second)).Should().BeTrue();
    }

    private static CreatePersonLinkRequest Request(Guid personId, Guid relatedPersonId, PersonLinkKind kind) =>
        new() { PersonId = personId, RelatedPersonId = relatedPersonId, Kind = kind };

    private static Task<int> InsertRawAsync(
        RelioDbContext dbContext,
        string ownerId,
        Guid personId,
        Guid relatedPersonId,
        string kind,
        DateTime now) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [PersonLinks] ([Id], [OwnerId], [PersonId], [RelatedPersonId], [Kind], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES ({Guid.NewGuid()}, {ownerId}, {personId}, {relatedPersonId}, {kind}, {now}, {now})
            """);

    private static async Task<string> IndexColumnsAsync(RelioDbContext dbContext, string indexName)
    {
        var columns = await dbContext.Database.SqlQuery<string>($"""
            SELECT c.name AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.PersonLinks') AND i.name = {indexName} AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal
            """).ToListAsync();
        return string.Join(",", columns);
    }
}
