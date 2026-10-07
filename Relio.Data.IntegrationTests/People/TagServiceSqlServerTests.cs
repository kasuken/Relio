using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Data.Configurations;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// Issue #25 against a real SQL Server: the usage count's query translation, the case-insensitive
/// unique index and the race it backstops, and that removing a tag takes it off people without
/// touching them. The InMemory twins are in <c>Relio.Data.Tests.People.TagServiceManagementTests</c>.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class TagServiceSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task ListWithUsageAsync_counts_people_per_tag()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var other = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        var climbing = await TestDataFactory.CreateTagAsync(dbContext, owner, "Climbing");
        var chess = await TestDataFactory.CreateTagAsync(dbContext, owner, "Chess");
        await TestDataFactory.CreateTagAsync(dbContext, owner, "Book club");
        var foreignTag = await TestDataFactory.CreateTagAsync(dbContext, other, "Climbing");
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada", tagIds: [climbing, chess]);
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Bo", isArchived: true, tagIds: [climbing]);
        await TestDataFactory.CreatePersonAsync(dbContext, other, "Di", tagIds: [foreignTag]);
        dbContext.ChangeTracker.Clear();

        var usage = await TestDataFactory.CreateTagService(dbContext, owner).ListWithUsageAsync();

        usage.Select(u => (u.Name, u.PeopleCount)).Should().Equal(("Book club", 0), ("Chess", 1), ("Climbing", 2));
    }

    [SqlServerFact]
    public async Task CreateAsync_refuses_case_only_duplicates()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        await TestDataFactory.CreateTagAsync(dbContext, owner, "Climbing");
        dbContext.ChangeTracker.Clear();

        var act = () => TestDataFactory.CreateTagService(dbContext, owner).CreateAsync("cLIMBING");

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(LabelValidationError.NameTaken);
    }

    [SqlServerFact]
    public async Task CreateAsync_reports_a_name_taken_at_the_same_moment_as_NameTaken_and_saves_nothing()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var interceptor = new InsertTagOnFirstSaveInterceptor(fixture, owner, "climbing");
        await using var dbContext = fixture.CreateDbContext(interceptor);

        var act = () => TestDataFactory.CreateTagService(dbContext, owner).CreateAsync("Climbing");

        var exception = (await act.Should().ThrowAsync<LabelValidationException>()).Which;
        exception.Error.Should().Be(LabelValidationError.NameTaken);
        exception.InnerException.Should().BeNull("the database error quotes the duplicate key");
        exception.Message.Should().NotContain("limbing");
        interceptor.Fired.Should().BeTrue();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        await using var fresh = fixture.CreateDbContext();
        (await fresh.Tags.Where(t => t.OwnerId == owner).Select(t => t.Name).ToListAsync()).Should().Equal("climbing");
    }

    [SqlServerFact]
    public async Task RenameAsync_reports_a_name_taken_at_the_same_moment_as_NameTaken()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid id;
        await using (var setup = fixture.CreateDbContext())
        {
            id = await TestDataFactory.CreateTagAsync(setup, owner, "Chess");
        }

        var interceptor = new InsertTagOnFirstSaveInterceptor(fixture, owner, "climbing");
        await using var dbContext = fixture.CreateDbContext(interceptor);

        var act = () => TestDataFactory.CreateTagService(dbContext, owner).RenameAsync(id, "Climbing");

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(LabelValidationError.NameTaken);
        await using var fresh = fixture.CreateDbContext();
        (await fresh.Tags.AsNoTracking().SingleAsync(t => t.Id == id)).Name.Should().Be("Chess");
    }

    [SqlServerFact]
    public async Task RenameAsync_changes_the_tag_every_person_sees()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid climbing, ada, bo;
        await using (var setup = fixture.CreateDbContext())
        {
            climbing = await TestDataFactory.CreateTagAsync(setup, owner, "Climbing");
            ada = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada", tagIds: [climbing]);
            bo = await TestDataFactory.CreatePersonAsync(setup, owner, "Bo", tagIds: [climbing]);
        }

        await using var dbContext = fixture.CreateDbContext();
        (await TestDataFactory.CreateTagService(dbContext, owner).RenameAsync(climbing, "  Bouldering ")).Should().BeTrue();

        await using var fresh = fixture.CreateDbContext();
        var people = await fresh.People.AsNoTracking().Include(p => p.Tags).Where(p => p.Id == ada || p.Id == bo).ToListAsync();
        people.SelectMany(p => p.Tags).Select(t => t.Name).Should().Equal("Bouldering", "Bouldering");
    }

    [SqlServerFact]
    public async Task RenameAsync_can_change_only_the_casing()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        var id = await TestDataFactory.CreateTagAsync(dbContext, owner, "chess");
        dbContext.ChangeTracker.Clear();

        (await TestDataFactory.CreateTagService(dbContext, owner).RenameAsync(id, "Chess")).Should().BeTrue();

        await using var fresh = fixture.CreateDbContext();
        (await fresh.Tags.AsNoTracking().SingleAsync(t => t.Id == id)).Name.Should().Be("Chess");
    }

    [SqlServerFact]
    public async Task DeleteAsync_removes_the_tag_and_its_links_but_not_the_people()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid climbing, chess, ada, bo;
        await using (var setup = fixture.CreateDbContext())
        {
            climbing = await TestDataFactory.CreateTagAsync(setup, owner, "Climbing");
            chess = await TestDataFactory.CreateTagAsync(setup, owner, "Chess");
            ada = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada", tagIds: [climbing, chess]);
            bo = await TestDataFactory.CreatePersonAsync(setup, owner, "Bo", isArchived: true, tagIds: [climbing]);
        }

        await using var dbContext = fixture.CreateDbContext();
        (await TestDataFactory.CreateTagService(dbContext, owner).DeleteAsync(climbing)).Should().BeTrue();

        await using var fresh = fixture.CreateDbContext();
        (await fresh.Tags.AnyAsync(t => t.Id == climbing)).Should().BeFalse();
        var people = await fresh.People.AsNoTracking().Include(p => p.Tags).Where(p => p.Id == ada || p.Id == bo).ToListAsync();
        people.Should().HaveCount(2);
        people.Single(p => p.Id == ada).Tags.Select(t => t.Name).Should().Equal("Chess");
        people.Single(p => p.Id == bo).Tags.Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task Deleting_a_tag_row_directly_cascades_to_person_links()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid climbing, ada;
        await using (var setup = fixture.CreateDbContext())
        {
            climbing = await TestDataFactory.CreateTagAsync(setup, owner, "Climbing");
            ada = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada", tagIds: [climbing]);
        }

        await using var dbContext = fixture.CreateDbContext();
        await dbContext.Tags.Where(t => t.Id == climbing).ExecuteDeleteAsync();

        (await dbContext.People.AsNoTracking().Include(p => p.Tags).SingleAsync(p => p.Id == ada)).Tags.Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task RenameAsync_and_DeleteAsync_for_another_owners_tag_return_false()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var other = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        var foreign = await TestDataFactory.CreateTagAsync(dbContext, other, "Secret");
        dbContext.ChangeTracker.Clear();
        var service = TestDataFactory.CreateTagService(dbContext, owner);

        (await service.RenameAsync(foreign, "Mine")).Should().BeFalse();
        (await service.DeleteAsync(foreign)).Should().BeFalse();

        await using var fresh = fixture.CreateDbContext();
        (await fresh.Tags.AsNoTracking().SingleAsync(t => t.Id == foreign)).Name.Should().Be("Secret");
    }

    [SqlServerFact]
    public async Task Tag_name_index_is_unique_and_named_as_the_service_expects()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await using var dbContext = fixture.CreateDbContext();
        await TestDataFactory.CreateTagAsync(dbContext, owner, "Climbing");

        dbContext.Tags.Add(new Tag { OwnerId = owner, Name = "CLIMBING" });
        var act = () => dbContext.SaveChangesAsync();

        var exception = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        exception.InnerException.Should().BeOfType<SqlException>().Which.Message
            .Should().Contain(TagConfiguration.NameIndexName);
    }
}
