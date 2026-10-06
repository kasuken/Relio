using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data;
using Relio.Data.People;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #23 end to end: the people list's sorting, archived view and paging through the real page,
/// with the view kept in the address. Tests that seed people register a fresh user, so the list
/// holds exactly what the test put there; the shared demo user's people are only ever read, and
/// nothing here asserts relative-date wording on them (it moves every day - that is covered in
/// bUnit with a fixed "today"). Absence is only asserted after the count line has appeared, since
/// the list loads after the circuit connects.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class PeopleListTests(RelioAppFixture fixture)
{
    private static ILocator Names(IPage page) => page.Locator("[data-testid='people-list-item-name']");

    private static ILocator Count(IPage page) => page.Locator("[data-testid='people-count']");

    private static ILocator ShowArchived(IPage page) => page.Locator("[data-testid='people-show-archived'] input");

    private static async Task ChooseSortAsync(IPage page, string option)
    {
        await page.Locator("[data-testid='people-sort'] .mud-input-control").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
    }

    private async Task<string> RegisterFreshUserAsync(IPage page, string prefix)
    {
        var email = NewEmail(prefix);
        await RegisterAsync(page, email, StrongPassword);
        var user = await GetUserAsync(fixture.App, email);
        return user!.Id;
    }

    [Fact]
    public async Task The_demo_user_sees_only_active_people_until_show_archived_is_on()
    {
        var page = await fixture.NewPageAsync();
        await RelioAppFixture.SignInAsDemoAsync(page);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(Count(page)).ToBeVisibleAsync();
        await Expect(Names(page).Filter(new() { HasTextString = "Ada Lovelace" })).ToHaveCountAsync(1);
        await Expect(Names(page).Filter(new() { HasTextString = "Sam Okafor" })).ToHaveCountAsync(0);

        await ShowArchived(page).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"/people\?archived=true$"));
        var sam = page.Locator("[data-testid='people-list-item']", new() { HasText = "Sam Okafor" });
        await Expect(sam).ToBeVisibleAsync();
        await Expect(sam.Locator("[data-testid='people-list-item-archived']")).ToHaveTextAsync("Archived");

        await ShowArchived(page).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"/people$"));
        await Expect(Names(page).Filter(new() { HasTextString = "Sam Okafor" })).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Sorting_is_kept_in_the_url_and_restored_by_back_navigation()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page, "sorting");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;
        await PeopleTestHelpers.SeedPeopleAsync(
            fixture.App,
            ownerId,
            new SeedPerson("Cora", LastContactedOn: today.AddDays(-30), CreatedAtUtc: now.AddDays(-3)),
            new SeedPerson("Abel", CreatedAtUtc: now.AddDays(-2)),
            new SeedPerson("Bryn", LastContactedOn: today.AddDays(-5), CreatedAtUtc: now.AddDays(-1)));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(Names(page)).ToHaveTextAsync(["Abel", "Bryn", "Cora"]);
        await Expect(page.Locator("[data-testid='people-sort'] input")).ToHaveValueAsync("Name");

        await ChooseSortAsync(page, "Last contacted");
        await Expect(page).ToHaveURLAsync(new Regex(@"/people\?sort=contacted$"));
        await Expect(Names(page)).ToHaveTextAsync(["Bryn", "Cora", "Abel"]);

        await ChooseSortAsync(page, "Recently added");
        await Expect(page).ToHaveURLAsync(new Regex(@"/people\?sort=added$"));
        await Expect(Names(page)).ToHaveTextAsync(["Bryn", "Abel", "Cora"]);

        await page.GoBackAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/people\?sort=contacted$"));
        await Expect(Names(page)).ToHaveTextAsync(["Bryn", "Cora", "Abel"]);
        await Expect(page.Locator("[data-testid='people-sort'] input")).ToHaveValueAsync("Last contacted");

        await page.GoBackAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/people$"));
        await Expect(Names(page)).ToHaveTextAsync(["Abel", "Bryn", "Cora"]);

        // A reload keeps the view: it lives in the address, not in the circuit.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people?sort=contacted");
        await Expect(Names(page)).ToHaveTextAsync(["Bryn", "Cora", "Abel"]);
        await page.ReloadAsync();
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(Names(page)).ToHaveTextAsync(["Bryn", "Cora", "Abel"]);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Paging_moves_between_pages_and_keeps_the_page_in_the_url()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page, "paging");
        await PeopleTestHelpers.SeedPeopleAsync(
            fixture.App,
            ownerId,
            Enumerable.Range(1, 51).Select(i => new SeedPerson("Pagetest", $"{i:00}")).ToArray());

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(Names(page)).ToHaveCountAsync(50);
        await Expect(Count(page)).ToHaveTextAsync("51 people");

        await page.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"/people\?page=2$"));
        await Expect(Names(page)).ToHaveCountAsync(1);
        await Expect(Names(page)).ToHaveTextAsync("Pagetest 51");
        await Expect(Count(page)).ToHaveTextAsync("51 people");

        await page.GoBackAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/people$"));
        await Expect(Names(page)).ToHaveCountAsync(50);

        // A page past the end is not an error: the last page shows, and the address is left alone.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people?page=9");
        await Expect(Names(page)).ToHaveTextAsync("Pagetest 51");
        await Expect(page).ToHaveURLAsync(new Regex(@"/people\?page=9$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task The_people_list_is_usable_on_a_phone()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        var ownerId = await RegisterFreshUserAsync(page, "phone");
        await PeopleTestHelpers.SeedPeopleAsync(
            fixture.App,
            ownerId,
            new SeedPerson("Abel", "Averyveryveryveryverylongsurnamethatmustwrapinsteadofoverflowing"),
            new SeedPerson("Sam", IsArchived: true));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(Count(page)).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='people-sort']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='people-show-archived']")).ToBeVisibleAsync();
        await AssertNoHorizontalScrollAsync(page);
        var box = await page.Locator("[data-testid='people-list-item']").First.BoundingBoxAsync();
        box!.Height.Should().BeGreaterThanOrEqualTo(44, "a row is a comfortable touch target");

        await ShowArchived(page).ClickAsync();

        await Expect(page.Locator("[data-testid='people-list-item-archived']")).ToBeVisibleAsync();
        await AssertNoHorizontalScrollAsync(page);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Another_users_people_never_appear_in_the_list()
    {
        var userA = await fixture.NewPageAsync();
        var ownerA = await RegisterFreshUserAsync(userA, "owner-a");
        await RelioAppFixture.ClosePageAsync(userA);
        var activeA = $"Alphonse{Guid.NewGuid():N}"[..16];
        var archivedA = $"Archibald{Guid.NewGuid():N}"[..16];
        await PeopleTestHelpers.SeedPeopleAsync(
            fixture.App, ownerA, new SeedPerson(activeA), new SeedPerson(archivedA, IsArchived: true));

        var pageB = await fixture.NewPageAsync();
        var ownerB = await RegisterFreshUserAsync(pageB, "owner-b");
        await PeopleTestHelpers.SeedPeopleAsync(fixture.App, ownerB, new SeedPerson("Bertram"));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, "/people?archived=true&sort=added");
        await Expect(Count(pageB)).ToHaveTextAsync("1 person, none archived");
        await Expect(Names(pageB)).ToHaveTextAsync("Bertram");
        var content = await pageB.ContentAsync();
        content.Should().NotContain(activeA).And.NotContain(archivedA);

        using var scope = fixture.App.CreateRealScope();
        var service = new PeopleService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new FixedCurrentUser(ownerB),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        var result = await service.ListPageAsync(new PeopleListQuery { IncludeArchived = true });
        result.People.TotalCount.Should().Be(1);
        result.ArchivedCount.Should().Be(0);
        result.People.Items.Select(p => p.FirstName).Should().Equal("Bertram");

        await RelioAppFixture.ClosePageAsync(pageB);
    }

    private static async Task AssertNoHorizontalScrollAsync(IPage page)
    {
        var overflows = await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth");
        overflows.Should().BeFalse("the page must never scroll sideways on a phone");
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }
}
