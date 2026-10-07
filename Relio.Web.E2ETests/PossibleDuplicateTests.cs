using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #27 end to end: adding or renaming a person warns when someone in the user's own list looks
/// like the same person, and "Save anyway" carries on. Every test registers its own fresh user, so
/// the people it adds are the only ones that user has and nothing here touches the shared demo user.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class PossibleDuplicateTests(RelioAppFixture fixture)
{
    private const string GuidPattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    private static readonly Regex ProfileUrl = new($"/people/{GuidPattern}$");

    private static readonly Regex NewPersonUrl = new("/people/new$");

    private static ILocator Warning(IPage page) => page.Locator("[data-testid='person-duplicate-warning']");

    private static ILocator Links(IPage page) => page.Locator("[data-testid='person-duplicate-link']");

    private async Task<string> RegisterFreshUserAsync(IPage page)
    {
        var email = NewEmail("dupes");
        await RegisterAsync(page, email, StrongPassword);
        return (await GetUserAsync(fixture.App, email))!.Id;
    }

    private static async Task SaveAsync(IPage page) =>
        await page.Locator("[data-testid='person-form-save']").ClickAsync();

    private static async Task SaveAnywayAsync(IPage page) =>
        await page.Locator("[data-testid='person-duplicate-save-anyway']").ClickAsync();

    [Fact]
    public async Task Adding_Jon_Smith_when_John_Smith_exists_warns_and_save_anyway_adds_him()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        await PeopleTestHelpers.SeedPeopleAsync(fixture.App, ownerId, new SeedPerson("John", "Smith"));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByLabel("First name").FillAsync("Jon");
        await page.GetByLabel("Last name").FillAsync("Smith");
        await SaveAsync(page);

        await Expect(Warning(page)).ToBeVisibleAsync();
        await Expect(Warning(page)).ToContainTextAsync("You may already have this person.");
        await Expect(Links(page)).ToHaveCountAsync(1);
        await Expect(Links(page)).ToContainTextAsync("John Smith");
        await Expect(Links(page)).ToHaveAttributeAsync("href", new Regex($"^/people/{GuidPattern}$"));
        await Expect(Links(page)).ToHaveAttributeAsync("target", "_blank");
        await Expect(page.Locator("[data-testid='person-duplicate-reason']")).ToHaveTextAsync("Similar name");
        await Expect(page).ToHaveURLAsync(NewPersonUrl);
        (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId)).Should().ContainSingle("nothing was saved yet");

        // The link opens John in a new tab and leaves the half-filled form alone.
        var popup = await page.Context.RunAndWaitForPageAsync(() => Links(page).ClickAsync());
        await Expect(popup.Locator("[data-testid='person-name']")).ToHaveTextAsync("John Smith");
        await popup.CloseAsync();
        await Expect(page.GetByLabel("First name")).ToHaveValueAsync("Jon");

        await SaveAnywayAsync(page);

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(page.Locator(".mud-snackbar", new() { HasText = "Person added" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync("Jon Smith");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(page.Locator("[data-testid='people-list-item']")).ToHaveCountAsync(2);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_person_with_no_similar_profile_is_saved_without_a_warning()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        await PeopleTestHelpers.SeedPeopleAsync(fixture.App, ownerId, new SeedPerson("John", "Smith"));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByLabel("First name").FillAsync("Ada");
        await page.GetByLabel("Last name").FillAsync("Lovelace");
        await SaveAsync(page);

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync("Ada Lovelace");
        await Expect(Warning(page)).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_matching_email_warns_even_with_a_different_name()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = "Ada",
            LastName = "Byron",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")],
        });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByLabel("First name").FillAsync("Grace");
        await page.GetByLabel("Last name").FillAsync("Hopper");
        await page.Locator("[data-testid='person-contact-method-add']").ClickAsync();
        await page.Locator("[data-testid='person-contact-method-value-field'] input").FillAsync("ADA@Example.com");
        await SaveAsync(page);

        await Expect(Warning(page)).ToBeVisibleAsync();
        await Expect(Links(page)).ToHaveCountAsync(1);
        await Expect(Links(page)).ToContainTextAsync("Ada Byron");
        await Expect(page.Locator("[data-testid='person-duplicate-reason']")).ToHaveTextAsync("Same email address");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Another_users_people_never_trigger_the_warning()
    {
        var emailA = NewEmail("dupes-a");
        var pageA = await fixture.NewPageAsync();
        await RegisterAsync(pageA, emailA, StrongPassword);
        var ownerA = (await GetUserAsync(fixture.App, emailA))!.Id;
        await RelioAppFixture.ClosePageAsync(pageA);
        await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerA, new CreatePersonRequest
        {
            FirstName = "John",
            LastName = "Smith",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "john@example.com")],
        });

        var pageB = await fixture.NewPageAsync();
        var ownerB = await RegisterFreshUserAsync(pageB);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, "/people/new");
        await pageB.GetByLabel("First name").FillAsync("John");
        await pageB.GetByLabel("Last name").FillAsync("Smith");
        await pageB.Locator("[data-testid='person-contact-method-add']").ClickAsync();
        await pageB.Locator("[data-testid='person-contact-method-value-field'] input").FillAsync("john@example.com");
        await SaveAsync(pageB);

        await Expect(pageB).ToHaveURLAsync(ProfileUrl);
        await Expect(Warning(pageB)).ToHaveCountAsync(0);
        (await PeopleTestHelpers.FindPossibleDuplicatesAsync(fixture.App, ownerB, new PossibleDuplicateQuery
        {
            FirstName = "John",
            LastName = "Smith",
            ExcludePersonId = (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerB)).Single().Id,
        })).Should().BeEmpty("user A's John Smith is not user B's business");

        await RelioAppFixture.ClosePageAsync(pageB);
    }

    [Fact]
    public async Task Renaming_a_person_to_match_someone_else_warns_and_save_anyway_saves()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = "John", LastName = "Smith" });
        var adaId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = "Ada", LastName = "Byron" });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{adaId}/edit");
        await page.GetByLabel("First name").FillAsync("Jon");
        await page.GetByLabel("Last name").FillAsync("Smith");
        await SaveAsync(page);

        await Expect(Warning(page)).ToBeVisibleAsync();
        await Expect(Links(page)).ToHaveCountAsync(1);
        await Expect(Links(page)).ToContainTextAsync("John Smith");
        await Expect(Links(page)).Not.ToContainTextAsync("Ada");

        await SaveAnywayAsync(page);

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{adaId}$"));
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync("Jon Smith");

        // Editing the details without renaming does not warn, although a John Smith still exists.
        await page.Locator("[data-testid='person-edit']").ClickAsync();
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await page.GetByLabel("How you met").FillAsync("At a conference.");
        await SaveAsync(page);

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{adaId}$"));
        await Expect(page.Locator("[data-testid='person-how-we-met']")).ToHaveTextAsync("At a conference.");
        await Expect(Warning(page)).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task An_archived_match_is_labelled_archived()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        await PeopleTestHelpers.SeedPeopleAsync(fixture.App, ownerId, new SeedPerson("John", "Smith", IsArchived: true));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByLabel("First name").FillAsync("John");
        await page.GetByLabel("Last name").FillAsync("Smith");
        await SaveAsync(page);

        await Expect(Warning(page)).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='person-duplicate-reason']")).ToContainTextAsync("Same name");
        await Expect(page.Locator("[data-testid='person-duplicate-reason']")).ToContainTextAsync("Archived");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task The_duplicate_warning_works_on_a_phone()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        var ownerId = await RegisterFreshUserAsync(page);
        await PeopleTestHelpers.SeedPeopleAsync(fixture.App, ownerId, new SeedPerson("John", "Smith"));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByLabel("First name").FillAsync("Jon");
        await page.GetByLabel("Last name").FillAsync("Smith");
        await SaveAsync(page);

        await Expect(Warning(page)).ToBeVisibleAsync();
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the warning must not scroll the page sideways on a phone");

        var saveAnyway = page.Locator("[data-testid='person-duplicate-save-anyway']");
        await saveAnyway.ScrollIntoViewIfNeededAsync();
        var buttonBox = await saveAnyway.BoundingBoxAsync();
        buttonBox!.Height.Should().BeGreaterThanOrEqualTo(44);
        buttonBox.Width.Should().BeGreaterThan((float)(Viewports.Phone.Width * 0.7), "the button is full width on a phone");

        var rowBox = await page.Locator("[data-testid='person-duplicate-item']").First.BoundingBoxAsync();
        rowBox!.Height.Should().BeGreaterThanOrEqualTo(44);

        await saveAnyway.ClickAsync();

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync("Jon Smith");

        await RelioAppFixture.ClosePageAsync(page);
    }
}
