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
/// Issue #22 end to end: a brand new user lists, adds and opens people through the real pages. Every
/// test registers its own fresh user (see <see cref="AccountTestHelpers"/>), so the people it adds are
/// the only ones that user has - and the registration itself proves the default relationship types
/// are seeded for new accounts.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class PeopleTests(RelioAppFixture fixture)
{
    private static readonly Regex ProfileUrl =
        new("/people/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

    private static string NewFirstName() => $"Ottilie{Guid.NewGuid():N}"[..16];

    private static ILocator Heading(IPage page, string name) =>
        page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true });

    /// <summary>Registers a fresh user (which signs them in) in <paramref name="page"/>.</summary>
    private static async Task RegisterNewUserAsync(IPage page, string prefix = "people")
    {
        await RegisterAsync(page, NewEmail(prefix), StrongPassword);
    }

    private static async Task OpenAddPersonAsync(IPage page) =>
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");

    private static async Task ChooseAsync(IPage page, string fieldTestId, string option)
    {
        await page.Locator($"[data-testid='{fieldTestId}'] .mud-input-control").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
    }

    [Fact]
    public async Task A_new_user_sees_the_empty_state_and_adds_a_person_with_only_a_first_name()
    {
        var firstName = NewFirstName();
        var page = await fixture.NewPageAsync();
        await RegisterNewUserAsync(page);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(Heading(page, "No one here yet")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Add a person", Exact = true }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/people/new$"));
        await Expect(Heading(page, "Add a person")).ToBeVisibleAsync();
        await page.GetByLabel("First name").FillAsync(firstName);
        await page.Locator("[data-testid='person-form-save']").ClickAsync();

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync(firstName);
        await Expect(Heading(page, firstName)).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='person-relationship']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='person-birthday']")).ToHaveCountAsync(0);
        await Expect(page.Locator(".mud-snackbar", new() { HasText = "Person added" })).ToBeVisibleAsync();

        await page.Locator("[data-testid='person-back']").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/people$"));
        await Expect(Heading(page, "People")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='people-list-item-name']")).ToHaveTextAsync(firstName);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_person_can_be_added_with_every_optional_detail()
    {
        var firstName = NewFirstName();
        var page = await fixture.NewPageAsync();
        await RegisterNewUserAsync(page);

        await OpenAddPersonAsync(page);
        await page.GetByLabel("First name").FillAsync(firstName);
        await page.GetByLabel("Last name").FillAsync("Lovelace");
        await page.GetByLabel("Nickname").FillAsync("Countess");
        // "Friend" being on offer proves registration seeded the default relationship types.
        await ChooseAsync(page, "person-relationship-field", "Friend");
        await page.Locator("[data-testid='person-birthday-day-field'] input").FillAsync("29");
        await ChooseAsync(page, "person-birthday-month-field", "February");
        await page.GetByLabel("How you met").FillAsync("At a talk about engines.");
        await page.GetByLabel("Details").FillAsync("Writes long letters.\nLikes music.");
        await page.Locator("[data-testid='person-form-save']").ClickAsync();

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync($"{firstName} Lovelace");
        await Expect(page.Locator("[data-testid='person-relationship']")).ToHaveTextAsync("Friend");
        await Expect(page.Locator("[data-testid='person-nickname']")).ToHaveTextAsync("Countess");
        await Expect(page.Locator("[data-testid='person-birthday']")).ToHaveTextAsync("29 February");
        await Expect(page.Locator("[data-testid='person-how-we-met']")).ToHaveTextAsync("At a talk about engines.");
        var details = page.Locator("[data-testid='person-details']");
        await Expect(details).ToContainTextAsync("Writes long letters.");
        await Expect(details).ToContainTextAsync("Likes music.");
        (await details.EvaluateAsync<string>("e => getComputedStyle(e).whiteSpace"))
            .Should().Be("pre-line", "line breaks in the details are kept");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        var row = page.Locator("[data-testid='people-list-item']", new() { HasText = firstName });
        await Expect(row.Locator("[data-testid='people-list-item-relationship']")).ToHaveTextAsync("Friend");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_missing_first_name_is_explained_and_nothing_is_saved()
    {
        var page = await fixture.NewPageAsync();
        await RegisterNewUserAsync(page);

        await OpenAddPersonAsync(page);
        await page.Locator("[data-testid='person-form-save']").ClickAsync();

        await Expect(page.Locator("[data-testid='person-first-name-field']")).ToContainTextAsync("Enter a first name.");
        await Expect(page).ToHaveURLAsync(new Regex("/people/new$"));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(Heading(page, "No one here yet")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_birthday_that_cannot_exist_or_has_not_happened_yet_is_explained()
    {
        var page = await fixture.NewPageAsync();
        await RegisterNewUserAsync(page);

        await OpenAddPersonAsync(page);
        await page.GetByLabel("First name").FillAsync(NewFirstName());
        await page.Locator("[data-testid='person-birthday-day-field'] input").FillAsync("31");
        await ChooseAsync(page, "person-birthday-month-field", "April");
        await page.Locator("[data-testid='person-form-save']").ClickAsync();
        await Expect(page.Locator("[data-testid='person-birthday-error']"))
            .ToHaveTextAsync("That date doesn't exist. Check the day, month and year.");

        await page.Locator("[data-testid='person-birthday-day-field'] input").FillAsync("1");
        await ChooseAsync(page, "person-birthday-month-field", "January");
        await page.Locator("[data-testid='person-birthday-year-field'] input").FillAsync("9999");
        await page.Locator("[data-testid='person-form-save']").ClickAsync();
        await Expect(page.Locator("[data-testid='person-birthday-error']"))
            .ToHaveTextAsync("Enter a date in the past or today.");
        await Expect(page).ToHaveURLAsync(new Regex("/people/new$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Adding_a_person_works_on_a_phone()
    {
        var firstName = NewFirstName();
        var page = await fixture.NewPageAsync(Viewports.Phone);
        await RegisterNewUserAsync(page);

        await OpenAddPersonAsync(page);

        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the form must not scroll sideways on a phone");
        var save = page.Locator("[data-testid='person-form-save']");
        await save.ScrollIntoViewIfNeededAsync();
        await Expect(save).ToBeVisibleAsync();
        var box = await save.BoundingBoxAsync();
        box!.Width.Should().BeGreaterThan((float)(Viewports.Phone.Width * 0.8), "the primary action is a full-width button on a phone");

        await page.GetByLabel("First name").FillAsync(firstName);
        await save.ClickAsync();

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(Heading(page, firstName)).ToBeVisibleAsync();
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the profile must not scroll sideways on a phone");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Another_users_person_link_shows_not_found()
    {
        var firstName = NewFirstName();
        var emailA = NewEmail("owner");
        var pageA = await fixture.NewPageAsync();
        await RegisterAsync(pageA, emailA, StrongPassword);
        await RelioAppFixture.ClosePageAsync(pageA);

        Guid personId;
        using (var scope = fixture.App.CreateRealScope())
        {
            var userA = (await GetUserAsync(fixture.App, emailA))!;
            var people = new PeopleService(
                scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
                new FixedCurrentUser(userA.Id),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            personId = (await people.CreateAsync(new CreatePersonRequest { FirstName = firstName, LastName = "Private" })).Id;
        }

        var pageB = await fixture.NewPageAsync();
        await RegisterNewUserAsync(pageB, "intruder");
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, $"/people/{personId}");

        await Expect(pageB.Locator("[data-testid='person-not-found']")).ToBeVisibleAsync();
        await Expect(Heading(pageB, "This person isn't in your list")).ToBeVisibleAsync();
        (await pageB.ContentAsync()).Should().NotContain(firstName).And.NotContain("Private");

        // A person that never existed looks exactly the same.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, $"/people/{Guid.NewGuid()}");
        await Expect(pageB.Locator("[data-testid='person-not-found']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(pageB);
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }
}
