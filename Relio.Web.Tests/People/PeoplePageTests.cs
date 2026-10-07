using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Paging;
using Relio.Application.People;
using Relio.Application.Time;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;
using PeoplePage = Relio.Web.Components.Pages.People;

namespace Relio.Web.Tests.People;

public class PeoplePageTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    // See ConfirmDialogTests for why the context is created per test with "await using".
    private static BunitContext CreateContext(FakePeopleService people, out IRenderedComponent<MudPopoverProvider> popovers)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("UTC", Today));
        popovers = context.Render<MudPopoverProvider>();
        return context;
    }

    private static BunitContext CreateContext(FakePeopleService people) => CreateContext(people, out _);

    /// <summary>Renders the page at <c>/people</c> plus <paramref name="queryString"/>, as the router would.</summary>
    private static IRenderedComponent<PeoplePage> RenderAt(BunitContext context, string queryString = "")
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/people" + queryString);
        return context.Render<PeoplePage>();
    }

    private static string CurrentPath(BunitContext context)
    {
        var uri = new Uri(context.Services.GetRequiredService<NavigationManager>().Uri);
        return uri.PathAndQuery;
    }

    private static PersonListItem Item(
        string firstName,
        string? lastName = null,
        string? relationship = null,
        DateOnly? lastContactedOn = null,
        bool archived = false) =>
        new(Guid.NewGuid(), firstName, lastName, relationship, lastContactedOn, archived, DateTime.UnixEpoch);

    private static Func<PeopleListQuery, PeopleListResult> Result(
        IReadOnlyList<PersonListItem> items, int active, int archived, int? total = null, int page = 1) =>
        query => new PeopleListResult(
            new PagedResult<PersonListItem>(items, page, query.PageSize, total ?? items.Count), active, archived);

    [Fact]
    public async Task Shows_the_empty_state_with_Add_a_person_when_there_are_no_people()
    {
        await using var context = CreateContext(new FakePeopleService());

        var cut = RenderAt(context);

        cut.Find("h1").TextContent.Should().Be("No one here yet");
        cut.Markup.Should().Contain("Add the first person you want to keep in touch with.");
        cut.Find("button").TextContent.Trim().Should().Be("Add a person");
        cut.FindAll("[data-testid='people-list']").Should().BeEmpty();
        cut.FindAll("[data-testid='people-toolbar']").Should().BeEmpty("there is nothing to sort or filter yet");
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task The_empty_state_offers_import()
    {
        await using var context = CreateContext(new FakePeopleService());

        var cut = RenderAt(context);

        var link = cut.Find("[data-testid='people-empty-import']");
        link.GetAttribute("href").Should().Be("/people/import");
        link.TextContent.Trim().Should().Be("Or import people from a file");
    }

    [Fact]
    public async Task The_header_links_to_import()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 0) };
        await using var context = CreateContext(people);

        var cut = RenderAt(context);

        var import = cut.Find("[data-testid='people-import']");
        import.GetAttribute("href").Should().Be("/people/import");
        import.TextContent.Trim().Should().Be("Import");
        cut.Find("[data-testid='people-add']").GetAttribute("href").Should().Be("/people/new");
        cut.Find(".rl-page-header-actions").Children.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_empty_state_action_navigates_to_the_create_page()
    {
        await using var context = CreateContext(new FakePeopleService());
        var cut = RenderAt(context);

        cut.Find("button").Click();

        CurrentPath(context).Should().Be("/people/new");
    }

    [Fact]
    public async Task Lists_people_with_initials_relationship_type_and_last_contacted()
    {
        var ada = Item("Ada", "Lovelace", "Friend", Today.AddDays(-12));
        var grace = Item("Grace");
        var people = new FakePeopleService { ListPageResult = Result([ada, grace], 2, 0) };
        await using var context = CreateContext(people);

        var cut = RenderAt(context);

        cut.Find("h1").TextContent.Should().Be("People");
        cut.FindAll("h1").Should().ContainSingle("the page has exactly one h1");
        var rows = cut.FindAll("[data-testid='people-list-item']");
        rows.Should().HaveCount(2);
        rows[0].GetAttribute("href").Should().Be($"/people/{ada.Id}");
        rows[0].QuerySelector("[data-testid='people-list-item-name']")!.TextContent.Should().Be("Ada Lovelace");
        rows[0].QuerySelector("[data-testid='people-list-item-relationship']")!.TextContent.Should().Be("Friend");
        rows[0].QuerySelector("[data-testid='people-list-item-last-contacted']")!.TextContent
            .Should().Be("Last contacted 12 days ago");
        rows[0].TextContent.Should().Contain("AL", "the monogram");
        rows[1].GetAttribute("href").Should().Be($"/people/{grace.Id}");
        rows[1].QuerySelector("[data-testid='people-list-item-relationship']").Should().BeNull();
        rows[1].QuerySelector("[data-testid='people-list-item-last-contacted']")!.TextContent
            .Should().Be("Not contacted yet");
        rows[1].QuerySelector(".rl-sep").Should().BeNull("the separator only sits between two facts");
        rows[0].QuerySelector(".rl-sep")!.GetAttribute("aria-hidden").Should().Be("true");
        cut.Find("[data-testid='people-add']").GetAttribute("href").Should().Be("/people/new");
        cut.Find("[data-testid='people-count']").TextContent.Should().Be("2 people");
    }

    [Fact]
    public async Task Archived_people_carry_an_Archived_label()
    {
        var people = new FakePeopleService
        {
            ListPageResult = Result([Item("Ada"), Item("Sam", archived: true)], 1, 1),
        };
        await using var context = CreateContext(people);

        var cut = RenderAt(context, "?archived=true");

        var rows = cut.FindAll("[data-testid='people-list-item']");
        rows[0].QuerySelector("[data-testid='people-list-item-archived']").Should().BeNull();
        var label = rows[1].QuerySelector("[data-testid='people-list-item-archived']")!;
        label.TextContent.Trim().Should().Be("Archived");
        label.QuerySelector("svg").Should().NotBeNull("the label has the archive icon");
        rows[1].QuerySelector(".rl-avatar-archived").Should().NotBeNull();
    }

    [Fact]
    public async Task Reads_sort_archived_and_page_from_the_query_string()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people);

        RenderAt(context, "?sort=contacted&archived=true&page=3");

        people.ListPageQueries.Should().ContainSingle().Which.Should().Be(new PeopleListQuery
        {
            Sort = PeopleSort.LastContacted,
            IncludeArchived = true,
            Page = 3,
        });
    }

    [Fact]
    public async Task Invalid_query_values_fall_back_to_the_defaults()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people);

        RenderAt(context, "?sort=bogus&archived=maybe&page=-4");

        people.ListPageQueries.Should().ContainSingle().Which.Should().Be(new PeopleListQuery());
    }

    [Fact]
    public async Task Changing_the_sort_navigates_with_the_sort_and_back_to_page_one()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 0) };
        await using var context = CreateContext(people, out var popovers);
        var cut = RenderAt(context, "?archived=true&page=3");

        cut.Find("[data-testid='people-sort'] .mud-input-control").MouseDown();
        popovers.WaitForElements(".mud-list-item")
            .Single(i => i.TextContent.Trim() == "Recently added").Click();

        CurrentPath(context).Should().Be("/people?sort=added&archived=true");
    }

    [Fact]
    public async Task The_sort_control_offers_the_three_orderings_and_shows_the_current_one()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 0) };
        await using var context = CreateContext(people, out var popovers);
        var cut = RenderAt(context, "?sort=contacted");

        cut.Find("[data-testid='people-sort'] input").GetAttribute("value").Should().Be("Last contacted");
        cut.Find("[data-testid='people-sort'] .mud-input-control").MouseDown();
        popovers.WaitForElements(".mud-list-item").Select(i => i.TextContent.Trim())
            .Should().Equal("Name", "Recently added", "Last contacted");
    }

    [Fact]
    public async Task Turning_on_show_archived_adds_it_to_the_url_and_resets_the_page()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 1) };
        await using var context = CreateContext(people);
        var cut = RenderAt(context, "?sort=added&page=2");

        cut.Find("[data-testid='people-show-archived'] input").Change(true);

        CurrentPath(context).Should().Be("/people?sort=added&archived=true");
    }

    [Fact]
    public async Task Turning_off_show_archived_removes_it_from_the_url()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 1) };
        await using var context = CreateContext(people);
        var cut = RenderAt(context, "?archived=true");

        cut.Find("[data-testid='people-show-archived'] input").Change(false);

        CurrentPath(context).Should().Be("/people");
    }

    [Fact]
    public async Task A_query_string_change_in_the_circuit_reloads_the_list()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 0) };
        await using var context = CreateContext(people);
        var cut = RenderAt(context);
        people.ListPageQueries.Should().HaveCount(1);

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/people?sort=added");

        cut.WaitForAssertion(() => people.ListPageQueries.Should().HaveCount(2));
        people.ListPageQueries[1].Sort.Should().Be(PeopleSort.RecentlyAdded);
    }

    [Fact]
    public async Task A_re_render_with_the_same_query_does_not_reload()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 0) };
        await using var context = CreateContext(people);
        var cut = RenderAt(context, "?sort=added");

        cut.Render();
        cut.Render();

        people.ListPageQueries.Should().HaveCount(1);
    }

    [Fact]
    public async Task When_everyone_is_archived_offers_to_show_them()
    {
        var people = new FakePeopleService
        {
            ListPageResult = query => query.IncludeArchived
                ? new PeopleListResult(new PagedResult<PersonListItem>([Item("Sam", archived: true)], 1, 50, 1), 0, 1)
                : new PeopleListResult(new PagedResult<PersonListItem>([], 1, 50, 0), 0, 1),
        };
        await using var context = CreateContext(people);

        var cut = RenderAt(context);

        cut.FindAll("h1").Should().ContainSingle().Which.TextContent.Should().Be("People");
        cut.Find("[data-testid='people-all-archived'] h2").TextContent.Should().Be("Everyone is archived");
        cut.FindAll("[data-testid='people-list']").Should().BeEmpty();
        cut.FindAll("[data-testid='people-count']").Should().BeEmpty();
        cut.Find("[data-testid='people-toolbar']").Should().NotBeNull("the switch is the way out");

        cut.Find("[data-testid='people-all-archived'] button").Click();

        CurrentPath(context).Should().Be("/people?archived=true");
    }

    [Fact]
    public async Task With_show_archived_on_and_no_one_archived_the_count_says_none_archived()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada"), Item("Grace")], 2, 0) };
        await using var context = CreateContext(people);

        var cut = RenderAt(context, "?archived=true");

        cut.Find("[data-testid='people-count']").TextContent.Should().Be("2 people, none archived");
    }

    [Fact]
    public async Task Pagination_is_hidden_on_a_single_page()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 0) };
        await using var context = CreateContext(people);

        var cut = RenderAt(context);

        cut.FindAll("[data-testid='people-pagination']").Should().BeEmpty();
    }

    [Fact]
    public async Task Pagination_shows_the_page_count_and_navigates_to_the_chosen_page()
    {
        var items = Enumerable.Range(1, 50).Select(i => Item($"Person {i:00}")).ToList();
        var people = new FakePeopleService { ListPageResult = Result(items, 120, 0, total: 120) };
        await using var context = CreateContext(people);
        var cut = RenderAt(context, "?sort=added");

        var pagination = cut.Find("[data-testid='people-pagination']");
        pagination.GetAttribute("aria-label").Should().Be("People pages");
        pagination.QuerySelectorAll("button").Select(b => b.TextContent.Trim())
            .Should().Contain(["1", "2", "3"], "three pages of 50 for 120 people");

        pagination.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "2").Click();

        CurrentPath(context).Should().Be("/people?sort=added&page=2");
    }

    [Fact]
    public async Task Pagination_is_labelled_flat_and_marks_the_current_page()
    {
        var items = Enumerable.Range(1, 50).Select(i => Item($"Person {i:00}")).ToList();
        var people = new FakePeopleService { ListPageResult = Result(items, 120, 0, total: 120, page: 2) };
        await using var context = CreateContext(people);

        var cut = RenderAt(context, "?page=2");

        var pagination = cut.Find("[data-testid='people-pagination']");
        pagination.QuerySelector("button[aria-current='page']")!.GetAttribute("aria-label").Should().Be("Current page 2");
        pagination.QuerySelector("button[aria-label='Page 1']").Should().NotBeNull();
        pagination.QuerySelector("button[aria-label='Next page']").Should().NotBeNull();
        pagination.QuerySelector("button[aria-label='Previous page']").Should().NotBeNull();
        pagination.QuerySelector("ul")!.ClassList.Should().Contain("mud-pagination-disable-elevation", "no shadows on page content");
    }

    [Fact]
    public async Task The_count_is_a_polite_status_region()
    {
        var people = new FakePeopleService { ListPageResult = Result([Item("Ada")], 1, 0) };
        await using var context = CreateContext(people);

        var cut = RenderAt(context);

        var count = cut.Find("[data-testid='people-count']");
        count.GetAttribute("role").Should().Be("status");
        count.TextContent.Should().Be("1 person");
    }
}
