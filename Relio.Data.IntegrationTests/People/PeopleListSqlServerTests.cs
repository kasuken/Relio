using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// What only a real SQL Server proves about the people list (issue #23): that every ordering
/// translates and behaves as documented (case-insensitive names, explicit nulls-last), that a
/// paged list never repeats or skips a row, and that the migration really created the indexes
/// and the column the list relies on. Every test seeds its own random owner, so they share the
/// run's one database without seeing each other.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PeopleListSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerTheory]
    [InlineData(PeopleSort.Name)]
    [InlineData(PeopleSort.RecentlyAdded)]
    [InlineData(PeopleSort.LastContacted)]
    public async Task ListPageAsync_translates_every_sort(PeopleSort sort)
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada", "Byron", new DateOnly(2026, 10, 1));
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Bea");
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Cleo", isArchived: true);

        var result = await TestDataFactory.CreateService(dbContext, owner)
            .ListPageAsync(new PeopleListQuery { Sort = sort, IncludeArchived = true, PageSize = 2, Page = 2 });

        result.People.TotalCount.Should().Be(3);
        result.People.Items.Should().HaveCount(1);
        result.ActiveCount.Should().Be(2);
        result.ArchivedCount.Should().Be(1);
    }

    [SqlServerFact]
    public async Task ListPageAsync_sorts_names_case_insensitively()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "carl");
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Bea");
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "ada");

        var result = await TestDataFactory.CreateService(dbContext, owner).ListPageAsync(new PeopleListQuery());

        result.People.Items.Select(p => p.FirstName).Should().Equal("ada", "Bea", "carl");
    }

    [SqlServerFact]
    public async Task ListPageAsync_sorts_recently_added_newest_first()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var now = DateTime.UtcNow;
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Oldest", createdAtUtc: now.AddDays(-3));
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Newest", createdAtUtc: now.AddDays(-1));
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Middle", createdAtUtc: now.AddDays(-2));

        var result = await TestDataFactory.CreateService(dbContext, owner)
            .ListPageAsync(new PeopleListQuery { Sort = PeopleSort.RecentlyAdded });

        result.People.Items.Select(p => p.FirstName).Should().Equal("Newest", "Middle", "Oldest");
    }

    [SqlServerFact]
    public async Task ListPageAsync_puts_never_contacted_people_last()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Zed");
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Older", lastContactedOn: new DateOnly(2026, 10, 1));
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Amy");
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Newer", lastContactedOn: new DateOnly(2026, 10, 5));

        var result = await TestDataFactory.CreateService(dbContext, owner)
            .ListPageAsync(new PeopleListQuery { Sort = PeopleSort.LastContacted });

        result.People.Items.Select(p => p.FirstName).Should().Equal("Newer", "Older", "Amy", "Zed");
    }

    [SqlServerTheory]
    [InlineData(PeopleSort.Name)]
    [InlineData(PeopleSort.RecentlyAdded)]
    [InlineData(PeopleSort.LastContacted)]
    public async Task ListPageAsync_pages_through_ties_without_repeats(PeopleSort sort)
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var sameMoment = DateTime.UtcNow.AddDays(-1);
        for (var i = 0; i < 5; i++)
        {
            // Identical in every sorted column: only the id tells them apart. Guid order is not
            // the same in SQL Server and .NET, so the test asserts distinctness, never an order.
            await TestDataFactory.CreatePersonAsync(dbContext, owner, "Sam", createdAtUtc: sameMoment);
        }

        var service = TestDataFactory.CreateService(dbContext, owner);
        var ids = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await service.ListPageAsync(new PeopleListQuery { Sort = sort, Page = page, PageSize = 2 });
            ids.AddRange(result.People.Items.Select(p => p.Id));
        }

        ids.Should().HaveCount(5).And.OnlyHaveUniqueItems();
    }

    [SqlServerFact]
    public async Task ListPageAsync_clamps_a_page_past_the_end()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Bea");
        await TestDataFactory.CreatePersonAsync(dbContext, owner, "Cleo");

        var result = await TestDataFactory.CreateService(dbContext, owner)
            .ListPageAsync(new PeopleListQuery { Page = 99, PageSize = 2 });

        result.People.Page.Should().Be(2);
        result.People.Items.Select(p => p.FirstName).Should().Equal("Cleo");
    }

    [SqlServerFact]
    public async Task People_list_indexes_exist_with_their_columns_in_order()
    {
        await using var dbContext = fixture.CreateDbContext();

        // One row per index: "name:Col1,Col2" with the key columns in key order. Included columns
        // are left out (is_included_column = 0), so only the key is compared.
        var indexes = await dbContext.Database.SqlQueryRaw<string>(
            """
            SELECT i.name + ':' + STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Value
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.People') AND ic.is_included_column = 0
            GROUP BY i.name
            """).ToListAsync();

        indexes.Should().Contain("IX_People_OwnerId_IsArchived_FirstName_LastName:OwnerId,IsArchived,FirstName,LastName");
        indexes.Should().Contain("IX_People_OwnerId_IsArchived_CreatedAtUtc:OwnerId,IsArchived,CreatedAtUtc");
        indexes.Should().Contain("IX_People_OwnerId_IsArchived_LastContactedOn:OwnerId,IsArchived,LastContactedOn");
        indexes.Should().NotContain(i => i.StartsWith("IX_People_OwnerId_IsArchived:", StringComparison.Ordinal),
            "the bare (OwnerId, IsArchived) index was replaced by the three composite ones");
    }

    [SqlServerFact]
    public async Task LastContactedOn_is_a_nullable_date_column()
    {
        await using var dbContext = fixture.CreateDbContext();

        var columns = await dbContext.Database.SqlQueryRaw<string>(
            """
            SELECT t.name + ':' + CASE WHEN c.is_nullable = 1 THEN 'null' ELSE 'not null' END AS Value
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.People') AND c.name = N'LastContactedOn'
            """).ToListAsync();

        columns.Should().Equal("date:null");
    }
}
