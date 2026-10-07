using Microsoft.EntityFrameworkCore;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// Proves what only a real SQL Server can for issue #22: the birthday check constraints, the unique
/// relationship type name per owner, the foreign key from a person to its relationship type and the
/// "clear it, don't delete the person" delete rule. The EF Core InMemory provider enforces none of
/// these, so <c>Relio.Data.Tests</c> cannot.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PersonSchemaSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Birthday_month_out_of_range_is_rejected_by_the_database()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        dbContext.People.Add(NewPerson(ownerId, birthdayDay: 1, birthdayMonth: 13));

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task Birthday_day_out_of_range_is_rejected_by_the_database()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        dbContext.People.Add(NewPerson(ownerId, birthdayDay: 32, birthdayMonth: 1));

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task Birthday_year_out_of_range_is_rejected_by_the_database()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        dbContext.People.Add(NewPerson(ownerId, birthdayDay: 1, birthdayMonth: 1, birthdayYear: 10000));

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task A_day_without_a_month_is_rejected_by_the_database()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        dbContext.People.Add(NewPerson(ownerId, birthdayDay: 10));

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task A_year_alone_is_rejected_by_the_database()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        dbContext.People.Add(NewPerson(ownerId, birthdayYear: 1990));

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task No_birthday_a_birthday_without_a_year_and_a_complete_birthday_are_all_accepted()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        dbContext.People.AddRange(
            NewPerson(ownerId),
            NewPerson(ownerId, birthdayDay: 29, birthdayMonth: 2),
            NewPerson(ownerId, birthdayDay: 10, birthdayMonth: 12, birthdayYear: 1815));

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [SqlServerFact]
    public async Task A_relationship_type_name_is_unique_per_owner()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        await TestDataFactory.CreateRelationshipTypeAsync(dbContext, ownerId, "Friend");

        dbContext.RelationshipTypes.Add(new RelationshipType { OwnerId = ownerId, Name = "Friend" });
        var act = () => dbContext.SaveChangesAsync();

        // Enforced by the unique (OwnerId, Name) index itself - InMemory does not honour unique indexes.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task Different_owners_can_each_have_Friend()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = await TestDataFactory.CreateOwnerAsync(fixture);
        var ownerB = await TestDataFactory.CreateOwnerAsync(fixture);
        await TestDataFactory.CreateRelationshipTypeAsync(dbContext, ownerA, "Friend");

        dbContext.RelationshipTypes.Add(new RelationshipType { OwnerId = ownerB, Name = "Friend" });
        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [SqlServerFact]
    public async Task A_person_cannot_point_at_a_relationship_type_that_does_not_exist()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        var person = NewPerson(ownerId);
        person.RelationshipTypeId = Guid.NewGuid();
        dbContext.People.Add(person);

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task Deleting_a_relationship_type_clears_it_from_people()
    {
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid typeId;
        Guid personId;
        await using (var setup = fixture.CreateDbContext())
        {
            typeId = await TestDataFactory.CreateRelationshipTypeAsync(setup, ownerId, "Friend");
            var person = NewPerson(ownerId);
            person.RelationshipTypeId = typeId;
            setup.People.Add(person);
            await setup.SaveChangesAsync();
            personId = person.Id;
        }

        await using (var delete = fixture.CreateDbContext())
        {
            delete.RelationshipTypes.Remove(await delete.RelationshipTypes.SingleAsync(t => t.Id == typeId));
            await delete.SaveChangesAsync();
        }

        await using var verify = fixture.CreateDbContext();
        var stillThere = await verify.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        stillThere.RelationshipTypeId.Should().BeNull("the person keeps existing, only the label is gone");
    }

    [SqlServerFact]
    public async Task Long_text_survives_at_its_column_limits()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        var person = NewPerson(ownerId);
        person.FirstName = new string('a', Person.FirstNameMaxLength);
        person.HowWeMet = new string('b', Person.HowWeMetMaxLength);
        person.Details = new string('c', Person.DetailsMaxLength);
        dbContext.People.Add(person);

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    private static Person NewPerson(
        string ownerId,
        int? birthdayDay = null,
        int? birthdayMonth = null,
        int? birthdayYear = null) =>
        new()
        {
            OwnerId = ownerId,
            FirstName = "Ada",
            BirthdayDay = birthdayDay,
            BirthdayMonth = birthdayMonth,
            BirthdayYear = birthdayYear,
        };
}
