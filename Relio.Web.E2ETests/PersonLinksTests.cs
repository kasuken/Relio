using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #129 end to end: connecting two profiles, seeing the reciprocal on the other one, editing,
/// removing, archived people and deleting a person, in a real browser over a real circuit. Every test
/// registers a fresh user and never touches the shared demo user's people.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class PersonLinksTests(RelioAppFixture fixture)
{
    private async Task<string> RegisterFreshUserAsync(IPage page)
    {
        var email = NewEmail("links");
        await RegisterAsync(page, email, StrongPassword);
        return (await GetUserAsync(fixture.App, email))!.Id;
    }

    private static async Task ChooseKindAsync(IPage page, string firstName, string kind)
    {
        await page.GetByRole(AriaRole.Combobox, new() { Name = $"What are they to {firstName}?", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = kind, Exact = true }).ClickAsync();
    }

    [Fact]
    public async Task Linking_two_people_shows_the_connection_on_both_profiles_with_the_reciprocal()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var anna = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = "Anna", LastName = "Rossi" });
        var marco = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = "Marco", LastName = "Rossi" });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{anna}");
        await Expect(page).ToHaveTitleAsync("Person - Relio");
        await Expect(page.GetByTestId("person-connections-empty")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Connections", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("h1")).ToHaveCountAsync(1);

        // Add: Marco is Anna's parent.
        await page.GetByTestId("person-add-connection").ClickAsync();
        var picker = page.GetByTestId("person-link-person-picker");
        await picker.ClickAsync();
        await picker.PressSequentiallyAsync("Mar");
        await page.GetByRole(AriaRole.Option, new() { Name = "Marco Rossi", Exact = true }).ClickAsync();
        await ChooseKindAsync(page, "Anna", "Parent");
        await page.GetByTestId("person-link-save").ClickAsync();

        await Expect(page.GetByTestId("person-connection-role")).ToHaveTextAsync("Parent");
        await Expect(page.GetByTestId("person-connection-link")).ToHaveTextAsync("Marco Rossi");
        var added = page.Locator(".mud-snackbar", new() { HasText = "Connection added" });
        await Expect(added).ToBeVisibleAsync();
        await Expect(added).Not.ToContainTextAsync("Marco");
        await Expect(page).ToHaveTitleAsync("Person - Relio");

        // The other profile shows the reciprocal.
        await page.GetByTestId("person-connection-link").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex($"/people/{marco}$"));
        await Expect(page.GetByTestId("person-name")).ToHaveTextAsync("Marco Rossi");
        await Expect(page.GetByTestId("person-connection-role")).ToHaveTextAsync("Child");
        await Expect(page.GetByTestId("person-connection-link")).ToHaveTextAsync("Anna Rossi");

        // Edit from this side: they are siblings.
        await page.GetByTestId("person-connection-edit").ClickAsync();
        await Expect(page.GetByTestId("person-link-other")).ToHaveTextAsync("Anna Rossi");
        await Expect(page.GetByTestId("person-link-person-picker")).ToHaveCountAsync(0);
        await ChooseKindAsync(page, "Marco", "Sibling");
        await page.GetByTestId("person-link-save").ClickAsync();
        await Expect(page.GetByTestId("person-connection-role")).ToHaveTextAsync("Sibling");
        var links = await PeopleTestHelpers.ListPersonLinksAsync(fixture.App, ownerId);
        links.Should().ContainSingle("editing never adds a second row").Which.Kind.Should().Be(PersonLinkKind.Sibling);

        await page.GetByTestId("person-connection-link").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex($"/people/{anna}$"));
        await Expect(page.GetByTestId("person-connection-role")).ToHaveTextAsync("Sibling");

        // Remove: asks first, and goes from both profiles.
        await page.GetByTestId("person-connection-remove").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToContainTextAsync("Remove this connection?");
        await Expect(page.Locator(".mud-dialog")).Not.ToContainTextAsync("Marco");
        (await PeopleTestHelpers.ListPersonLinksAsync(fixture.App, ownerId)).Should().HaveCount(1, "nothing goes before the confirmation");
        await page.GetByTestId("confirm-dialog-confirm").ClickAsync();
        await Expect(page.GetByTestId("person-connections-empty")).ToBeVisibleAsync();
        var removed = page.Locator(".mud-snackbar", new() { HasText = "Connection removed" });
        await Expect(removed).ToBeVisibleAsync();
        await Expect(removed).Not.ToContainTextAsync("Marco");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{marco}");
        await Expect(page.GetByTestId("person-connections-empty")).ToBeVisibleAsync();
        (await PeopleTestHelpers.ListPersonLinksAsync(fixture.App, ownerId)).Should().BeEmpty();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Archived_people_are_not_offered_but_existing_connections_stay_visible()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var anna = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = "Anna", LastName = "Rossi" });
        var marco = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = "Marco", LastName = "Rossi" });
        await PeopleTestHelpers.CreatePersonLinkAsync(fixture.App, ownerId, anna, marco, PersonLinkKind.Friend);
        await PeopleTestHelpers.ArchivePersonAsync(fixture.App, ownerId, marco);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{anna}");
        await Expect(page.GetByTestId("person-connection-link")).ToHaveTextAsync("Marco Rossi");
        await Expect(page.GetByTestId("person-connection-archived")).ToBeVisibleAsync();

        await page.GetByTestId("person-add-connection").ClickAsync();
        await page.GetByTestId("person-link-person-picker").ClickAsync();
        await Expect(page.GetByText("No other active people to connect.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Option, new() { Name = "Marco Rossi" })).ToHaveCountAsync(0);
        await page.GetByTestId("person-link-cancel").ClickAsync();

        // The archived person's own profile keeps the connection but offers no way to add one.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{marco}");
        await Expect(page.GetByTestId("person-connection-link")).ToHaveTextAsync("Anna Rossi");
        await Expect(page.GetByTestId("person-add-connection")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Deleting_a_person_removes_their_connections()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var anna = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = "Anna", LastName = "Rossi" });
        var marco = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = "Marco", LastName = "Rossi" });
        await PeopleTestHelpers.CreatePersonLinkAsync(fixture.App, ownerId, anna, marco, PersonLinkKind.Colleague);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{marco}");
        await page.Locator("[data-testid='person-actions']").GetByRole(AriaRole.Button, new() { Name = "More", Exact = true }).ClickAsync();
        await page.GetByTestId("person-delete").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToContainTextAsync("connections");
        await page.GetByTestId("confirm-dialog-confirm").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/people$"));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{anna}");
        await Expect(page.GetByTestId("person-connections-empty")).ToBeVisibleAsync();
        (await PeopleTestHelpers.ListPersonLinksAsync(fixture.App, ownerId)).Should().BeEmpty();
        (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, anna)).Should().NotBeNull("the other person stays");

        await RelioAppFixture.ClosePageAsync(page);
    }
}
