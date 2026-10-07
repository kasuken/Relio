using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Data.Configurations;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// Issue #25 against a real SQL Server: the usage count's query translation, the case-insensitive
/// unique index and the race it backstops, and that removing a type with people is one save that
/// either lands completely or not at all. The InMemory twins are in
/// <c>Relio.Data.Tests.People.RelationshipTypeServiceManagementTests</c>.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RelationshipTypeServiceSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task ListWithUsageAsync_translates_and_counts_people_including_archived()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var other = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        var friend = await TestDataFactory.CreateRelationshipTypeAsync(dbContext, owner, "Friend", 0);
        var family = await TestDataFactory.CreateRelationshipTypeAsync(dbContext, owner, "Family", 1);
        await TestDataFactory.CreateRelationshipTypeAsync(dbContext, owner, "Other", 2);
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada", relationshipTypeId: friend);
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Bo", isArchived: true, relationshipTypeId: friend);
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Cy", relationshipTypeId: family);
        await TestDataFactory.CreatePersonAsync(dbContext, other, "Di", relationshipTypeId: friend);
        dbContext.ChangeTracker.Clear();

        var usage = await TestDataFactory.CreateRelationshipTypeService(dbContext, owner).ListWithUsageAsync();

        usage.Select(u => (u.Name, u.PeopleCount)).Should().Equal(("Friend", 2), ("Family", 1), ("Other", 0));
    }

    [SqlServerFact]
    public async Task CreateAsync_refuses_a_name_that_differs_only_in_case()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        await TestDataFactory.CreateRelationshipTypeAsync(dbContext, owner, "Friend", 0);
        dbContext.ChangeTracker.Clear();

        var act = () => TestDataFactory.CreateRelationshipTypeService(dbContext, owner).CreateAsync("fRIEND");

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(LabelValidationError.NameTaken);
    }

    [SqlServerFact]
    public async Task RenameAsync_can_change_only_the_casing()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        var id = await TestDataFactory.CreateRelationshipTypeAsync(dbContext, owner, "friend", 0);
        dbContext.ChangeTracker.Clear();

        (await TestDataFactory.CreateRelationshipTypeService(dbContext, owner).RenameAsync(id, "Friend")).Should().BeTrue();

        await using var fresh = fixture.CreateDbContext();
        (await fresh.RelationshipTypes.AsNoTracking().SingleAsync(t => t.Id == id)).Name.Should().Be("Friend");
    }

    [SqlServerFact]
    public async Task CreateAsync_reports_a_name_taken_at_the_same_moment_as_NameTaken_and_saves_nothing()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        // Just before the save of "Mentor", another request creates "mentor" - which the unique
        // index then refuses.
        var interceptor = new RunOnFirstSaveInterceptor(async ct =>
        {
            await using var other = fixture.CreateDbContext();
            other.RelationshipTypes.Add(new RelationshipType { OwnerId = owner, Name = "mentor" });
            await other.SaveChangesAsync(ct);
        });
        await using var dbContext = fixture.CreateDbContext(interceptor);

        var act = () => TestDataFactory.CreateRelationshipTypeService(dbContext, owner).CreateAsync("Mentor");

        var exception = (await act.Should().ThrowAsync<LabelValidationException>()).Which;
        exception.Error.Should().Be(LabelValidationError.NameTaken);
        exception.InnerException.Should().BeNull("the database error quotes the duplicate key");
        exception.Message.Should().NotContain("entor");
        interceptor.Fired.Should().BeTrue();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        await using var fresh = fixture.CreateDbContext();
        (await fresh.RelationshipTypes.Where(t => t.OwnerId == owner).Select(t => t.Name).ToListAsync()).Should().Equal("mentor");
    }

    [SqlServerFact]
    public async Task RenameAsync_reports_a_name_taken_at_the_same_moment_as_NameTaken()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid id;
        await using (var setup = fixture.CreateDbContext())
        {
            id = await TestDataFactory.CreateRelationshipTypeAsync(setup, owner, "Friend", 0);
        }

        var interceptor = new RunOnFirstSaveInterceptor(async ct =>
        {
            await using var other = fixture.CreateDbContext();
            other.RelationshipTypes.Add(new RelationshipType { OwnerId = owner, Name = "mentor", SortOrder = 1 });
            await other.SaveChangesAsync(ct);
        });
        await using var dbContext = fixture.CreateDbContext(interceptor);

        var act = () => TestDataFactory.CreateRelationshipTypeService(dbContext, owner).RenameAsync(id, "Mentor");

        var exception = (await act.Should().ThrowAsync<LabelValidationException>()).Which;
        exception.Error.Should().Be(LabelValidationError.NameTaken);
        exception.InnerException.Should().BeNull();
        await using var fresh = fixture.CreateDbContext();
        (await fresh.RelationshipTypes.AsNoTracking().SingleAsync(t => t.Id == id)).Name.Should().Be("Friend");
    }

    [SqlServerFact]
    public async Task DeleteAsync_moves_people_and_removes_the_type_in_one_save()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid friend, acquaintance, ada, bo;
        await using (var setup = fixture.CreateDbContext())
        {
            friend = await TestDataFactory.CreateRelationshipTypeAsync(setup, owner, "Friend", 0);
            acquaintance = await TestDataFactory.CreateRelationshipTypeAsync(setup, owner, "Acquaintance", 1);
            ada = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada", relationshipTypeId: friend);
            bo = await TestDataFactory.CreatePersonAsync(setup, owner, "Bo", isArchived: true, relationshipTypeId: friend);
        }

        var counter = new CountSavesInterceptor();
        await using var dbContext = fixture.CreateDbContext(counter);
        (await TestDataFactory.CreateRelationshipTypeService(dbContext, owner).DeleteAsync(friend, acquaintance)).Should().BeTrue();

        counter.Saved.Should().Be(1);
        await using var fresh = fixture.CreateDbContext();
        (await fresh.RelationshipTypes.AnyAsync(t => t.Id == friend)).Should().BeFalse();
        (await fresh.People.AsNoTracking().Where(p => p.Id == ada || p.Id == bo).Select(p => p.RelationshipTypeId).ToListAsync())
            .Should().Equal(acquaintance, acquaintance);
    }

    [SqlServerFact]
    public async Task DeleteAsync_that_fails_in_the_database_changes_nothing()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid friend, acquaintance, ada;
        await using (var setup = fixture.CreateDbContext())
        {
            friend = await TestDataFactory.CreateRelationshipTypeAsync(setup, owner, "Friend", 0);
            acquaintance = await TestDataFactory.CreateRelationshipTypeAsync(setup, owner, "Acquaintance", 1);
            ada = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada", relationshipTypeId: friend);
        }

        await using var dbContext = fixture.CreateDbContext(new FailDeleteFromInterceptor("RelationshipTypes"));
        var act = () => TestDataFactory.CreateRelationshipTypeService(dbContext, owner).DeleteAsync(friend, acquaintance);

        (await act.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<InvalidOperationException>();

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        await using var fresh = fixture.CreateDbContext();
        (await fresh.RelationshipTypes.AnyAsync(t => t.Id == friend)).Should().BeTrue();
        (await fresh.People.AsNoTracking().SingleAsync(p => p.Id == ada)).RelationshipTypeId
            .Should().Be(friend, "the move was rolled back with the removal");
    }

    [SqlServerFact]
    public async Task DeleteAsync_without_a_target_clears_people()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid friend, ada;
        await using (var setup = fixture.CreateDbContext())
        {
            friend = await TestDataFactory.CreateRelationshipTypeAsync(setup, owner, "Friend", 0);
            ada = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada", relationshipTypeId: friend);
        }

        await using var dbContext = fixture.CreateDbContext();
        (await TestDataFactory.CreateRelationshipTypeService(dbContext, owner).DeleteAsync(friend, null)).Should().BeTrue();

        await using var fresh = fixture.CreateDbContext();
        var person = await fresh.People.AsNoTracking().SingleAsync(p => p.Id == ada);
        person.RelationshipTypeId.Should().BeNull();
        person.FirstName.Should().Be("Ada");
    }

    [SqlServerFact]
    public async Task A_person_assigned_while_the_type_is_being_deleted_is_cleared_by_the_database()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid friend;
        await using (var setup = fixture.CreateDbContext())
        {
            friend = await TestDataFactory.CreateRelationshipTypeAsync(setup, owner, "Friend", 0);
        }

        Guid newcomer = Guid.Empty;
        var interceptor = new RunOnFirstSaveInterceptor(async ct =>
        {
            await using var other = fixture.CreateDbContext();
            newcomer = await TestDataFactory.CreatePersonAsync(other, owner, "Late", relationshipTypeId: friend);
        });
        await using var dbContext = fixture.CreateDbContext(interceptor);

        (await TestDataFactory.CreateRelationshipTypeService(dbContext, owner).DeleteAsync(friend, null)).Should().BeTrue();

        interceptor.Fired.Should().BeTrue();
        await using var fresh = fixture.CreateDbContext();
        var person = await fresh.People.AsNoTracking().SingleAsync(p => p.Id == newcomer);
        person.RelationshipTypeId.Should().BeNull("the foreign key's ON DELETE SET NULL is the backstop");
    }

    [SqlServerFact]
    public async Task DeleteAsync_cannot_move_people_to_another_owners_type()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var other = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid friend, foreign, ada;
        await using (var setup = fixture.CreateDbContext())
        {
            friend = await TestDataFactory.CreateRelationshipTypeAsync(setup, owner, "Friend", 0);
            foreign = await TestDataFactory.CreateRelationshipTypeAsync(setup, other, "Mentor", 0);
            ada = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada", relationshipTypeId: friend);
        }

        await using var dbContext = fixture.CreateDbContext();
        var act = () => TestDataFactory.CreateRelationshipTypeService(dbContext, owner).DeleteAsync(friend, foreign);

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();
        await using var fresh = fixture.CreateDbContext();
        (await fresh.People.AsNoTracking().SingleAsync(p => p.Id == ada)).RelationshipTypeId.Should().Be(friend);
        (await fresh.RelationshipTypes.AnyAsync(t => t.Id == friend)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task RenameAsync_and_DeleteAsync_for_another_owners_type_return_false()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var other = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        var foreign = await TestDataFactory.CreateRelationshipTypeAsync(dbContext, other, "Mentor", 0);
        dbContext.ChangeTracker.Clear();
        var service = TestDataFactory.CreateRelationshipTypeService(dbContext, owner);

        (await service.RenameAsync(foreign, "Mine")).Should().BeFalse();
        (await service.DeleteAsync(foreign, null)).Should().BeFalse();

        await using var fresh = fixture.CreateDbContext();
        (await fresh.RelationshipTypes.AsNoTracking().SingleAsync(t => t.Id == foreign)).Name.Should().Be("Mentor");
    }

    [SqlServerFact]
    public async Task Relationship_type_name_index_is_unique_and_named_as_the_service_expects()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        await TestDataFactory.CreateRelationshipTypeAsync(dbContext, owner, "Friend", 0);

        dbContext.RelationshipTypes.Add(new RelationshipType { OwnerId = owner, Name = "FRIEND", SortOrder = 1 });
        var act = () => dbContext.SaveChangesAsync();

        var exception = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        exception.InnerException.Should().BeOfType<SqlException>().Which.Message
            .Should().Contain(RelationshipTypeConfiguration.NameIndexName);
    }
}
