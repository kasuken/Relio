using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.People;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #23 through <see cref="PeopleService.ListPageAsync"/>: the three orderings, archived
/// people, paging and clamping, and the projection. Runs on the InMemory provider, which sorts
/// strings case-sensitively and orders Guids differently from SQL Server - so tie-break tests
/// assert distinctness and stability, never a particular Guid order, and case-insensitive name
/// sorting is proven against SQL Server in <c>Relio.Data.IntegrationTests</c>.
/// </summary>
public class PeopleServiceListPageTests
{
    private const string Owner = "owner-1";

    private static readonly DateTimeOffset Start = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListPageAsync_excludes_archived_people_by_default_but_counts_them()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        await AddAsync(dbContext, "Ada");
        await AddAsync(dbContext, "Sam", isArchived: true);

        var result = await service.ListPageAsync(new PeopleListQuery());

        result.People.Items.Select(p => p.FirstName).Should().Equal("Ada");
        result.People.TotalCount.Should().Be(1);
        result.ActiveCount.Should().Be(1);
        result.ArchivedCount.Should().Be(1);
        result.HasAnyone.Should().BeTrue();
    }

    [Fact]
    public async Task ListPageAsync_includes_and_flags_archived_people_when_asked()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        await AddAsync(dbContext, "Ada");
        await AddAsync(dbContext, "Sam", isArchived: true);

        var result = await service.ListPageAsync(new PeopleListQuery { IncludeArchived = true });

        result.People.Items.Select(p => (p.FirstName, p.IsArchived)).Should().Equal(("Ada", false), ("Sam", true));
        result.People.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListPageAsync_sorts_by_first_name_then_last_name()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        await AddAsync(dbContext, "Bea");
        await AddAsync(dbContext, "Ada", "Lovelace");
        await AddAsync(dbContext, "Ada");
        await AddAsync(dbContext, "Ada", "Byron");

        var result = await service.ListPageAsync(new PeopleListQuery { Sort = PeopleSort.Name });

        result.People.Items.Select(p => p.DisplayName).Should().Equal("Ada", "Ada Byron", "Ada Lovelace", "Bea");
    }

    [Fact]
    public async Task ListPageAsync_sorts_recently_added_newest_first()
    {
        var (dbContext, service, time) = Create();
        await using var disposeContext = dbContext;
        await AddAsync(dbContext, "First");
        time.Advance(TimeSpan.FromMinutes(1));
        await AddAsync(dbContext, "Second");
        time.Advance(TimeSpan.FromMinutes(1));
        await AddAsync(dbContext, "Third");

        var result = await service.ListPageAsync(new PeopleListQuery { Sort = PeopleSort.RecentlyAdded });

        result.People.Items.Select(p => p.FirstName).Should().Equal("Third", "Second", "First");
    }

    [Fact]
    public async Task ListPageAsync_sorts_last_contacted_most_recent_first_and_never_contacted_last_by_name()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        await AddAsync(dbContext, "Older", lastContactedOn: new DateOnly(2026, 10, 1));
        await AddAsync(dbContext, "Zed");
        await AddAsync(dbContext, "Newer", lastContactedOn: new DateOnly(2026, 10, 5));
        await AddAsync(dbContext, "Amy");

        var result = await service.ListPageAsync(new PeopleListQuery { Sort = PeopleSort.LastContacted });

        result.People.Items.Select(p => p.FirstName).Should().Equal("Newer", "Older", "Amy", "Zed");
    }

    [Theory]
    [InlineData(PeopleSort.Name)]
    [InlineData(PeopleSort.RecentlyAdded)]
    [InlineData(PeopleSort.LastContacted)]
    public async Task ListPageAsync_breaks_ties_by_id_so_pages_never_repeat_or_skip(PeopleSort sort)
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;

        // Five people the sort cannot tell apart: same name, same creation instant, never contacted.
        for (var i = 0; i < 5; i++)
        {
            dbContext.People.Add(new Person { OwnerId = Owner, FirstName = "Sam" });
        }

        await dbContext.SaveChangesAsync();

        async Task<List<Guid>> ReadAllPagesAsync()
        {
            var ids = new List<Guid>();
            for (var page = 1; page <= 3; page++)
            {
                var result = await service.ListPageAsync(new PeopleListQuery { Sort = sort, Page = page, PageSize = 2 });
                ids.AddRange(result.People.Items.Select(p => p.Id));
            }

            return ids;
        }

        var first = await ReadAllPagesAsync();
        var second = await ReadAllPagesAsync();

        first.Should().OnlyHaveUniqueItems().And.HaveCount(5);
        second.Should().Equal(first, "the same query always returns the same order");
    }

    [Fact]
    public async Task ListPageAsync_pages_with_the_requested_size_and_reports_totals()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        foreach (var name in new[] { "A", "B", "C", "D", "E" })
        {
            await AddAsync(dbContext, name);
        }

        var second = await service.ListPageAsync(new PeopleListQuery { Page = 2, PageSize = 2 });

        second.People.Items.Select(p => p.FirstName).Should().Equal("C", "D");
        second.People.Page.Should().Be(2);
        second.People.PageSize.Should().Be(2);
        second.People.TotalCount.Should().Be(5);
        second.People.PageCount.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ListPageAsync_clamps_a_page_below_one_to_the_first_page(int page)
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        await AddAsync(dbContext, "Ada");
        await AddAsync(dbContext, "Bea");

        var result = await service.ListPageAsync(new PeopleListQuery { Page = page, PageSize = 1 });

        result.People.Page.Should().Be(1);
        result.People.Items.Select(p => p.FirstName).Should().Equal("Ada");
    }

    [Fact]
    public async Task ListPageAsync_clamps_a_page_past_the_end_to_the_last_page()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        foreach (var name in new[] { "A", "B", "C", "D", "E" })
        {
            await AddAsync(dbContext, name);
        }

        var result = await service.ListPageAsync(new PeopleListQuery { Page = 99, PageSize = 2 });

        result.People.Page.Should().Be(3);
        result.People.Items.Select(p => p.FirstName).Should().Equal("E");
    }

    [Fact]
    public async Task ListPageAsync_with_no_people_returns_an_empty_first_page()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;

        var result = await service.ListPageAsync(new PeopleListQuery { Page = 7 });

        result.People.Items.Should().BeEmpty();
        result.People.Page.Should().Be(1);
        result.People.TotalCount.Should().Be(0);
        result.People.PageCount.Should().Be(1);
        result.HasAnyone.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1000, PeopleListQuery.MaxPageSize)]
    public async Task ListPageAsync_clamps_the_page_size(int requested, int expected)
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        await AddAsync(dbContext, "Ada");

        var result = await service.ListPageAsync(new PeopleListQuery { PageSize = requested });

        result.People.PageSize.Should().Be(expected);
    }

    [Fact]
    public async Task ListPageAsync_rejects_an_unknown_sort()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;

        var act = () => service.ListPageAsync(new PeopleListQuery { Sort = (PeopleSort)99 });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ListPageAsync_rejects_a_null_query()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;

        var act = () => service.ListPageAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ListPageAsync_projects_the_relationship_type_name_and_last_contacted_date()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        var type = new RelationshipType { OwnerId = Owner, Name = "Friend" };
        dbContext.RelationshipTypes.Add(type);
        await dbContext.SaveChangesAsync();
        await AddAsync(dbContext, "Ada", "Lovelace", relationshipTypeId: type.Id, lastContactedOn: new DateOnly(2026, 10, 5));
        await AddAsync(dbContext, "Grace");

        var result = await service.ListPageAsync(new PeopleListQuery());

        var ada = result.People.Items.Single(p => p.FirstName == "Ada");
        ada.RelationshipTypeName.Should().Be("Friend");
        ada.LastContactedOn.Should().Be(new DateOnly(2026, 10, 5));
        ada.LastName.Should().Be("Lovelace");
        ada.IsArchived.Should().BeFalse();
        ada.CreatedAtUtc.Should().Be(Start.UtcDateTime);

        var grace = result.People.Items.Single(p => p.FirstName == "Grace");
        grace.RelationshipTypeName.Should().BeNull();
        grace.LastContactedOn.Should().BeNull();
    }

    [Fact]
    public async Task ListPageAsync_leaves_nothing_tracked()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        await AddAsync(dbContext, "Ada");
        dbContext.ChangeTracker.Clear();

        await service.ListPageAsync(new PeopleListQuery());

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ListPageAsync_reflects_an_archive_made_earlier_in_the_same_context()
    {
        var (dbContext, service, _) = Create();
        await using var disposeContext = dbContext;
        var id = await AddAsync(dbContext, "Ada");
        (await service.ListPageAsync(new PeopleListQuery())).ActiveCount.Should().Be(1);

        await service.ArchiveAsync(id);

        var result = await service.ListPageAsync(new PeopleListQuery());
        result.ActiveCount.Should().Be(0);
        result.ArchivedCount.Should().Be(1);
        result.People.Items.Should().BeEmpty();
    }

    private static (RelioDbContext DbContext, PeopleService Service, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider(Start);
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new RelioDbContext(options, time, FieldProtector);
        return (dbContext, new PeopleService(dbContext, new FakeCurrentUser(Owner), time), time);
    }

    private static async Task<Guid> AddAsync(
        RelioDbContext dbContext,
        string firstName,
        string? lastName = null,
        bool isArchived = false,
        DateOnly? lastContactedOn = null,
        Guid? relationshipTypeId = null)
    {
        var person = new Person
        {
            OwnerId = Owner,
            FirstName = firstName,
            LastName = lastName,
            IsArchived = isArchived,
            LastContactedOn = lastContactedOn,
            RelationshipTypeId = relationshipTypeId,
        };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }
}
