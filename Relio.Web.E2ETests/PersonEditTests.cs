using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #24 end to end: contact methods, tags and the relationship type, added, edited and removed
/// through the real form and read back on the real profile, in a real browser and over a real
/// circuit. Every test registers its own fresh user and uses unique names, so nothing here touches
/// the shared demo user's people or depends on another test's tags.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class PersonEditTests(RelioAppFixture fixture)
{
    private static readonly Regex EditUrl =
        new("/people/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/edit$");

    private static readonly Regex ProfileUrl =
        new("/people/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

    private static string NewFirstName() => $"Ottilie{Guid.NewGuid():N}"[..16];

    private static ILocator Rows(IPage page) => page.Locator("[data-testid='person-contact-method-row']");

    private static ILocator ValueOf(ILocator row) =>
        row.Locator("[data-testid='person-contact-method-value-field'] input, [data-testid='person-contact-method-value-field'] textarea");

    private static ILocator Headings(IPage page) => page.Locator("[data-testid='person-contact-method-heading']");

    private static ILocator TagField(IPage page) => page.Locator("[data-testid='person-tags-field'] input");

    private static ILocator Chips(IPage page) => page.Locator("[data-testid='person-tag-chip']");

    private static ILocator Options(IPage page) => page.Locator(".mud-popover-open .mud-list-item");

    private async Task<string> RegisterFreshUserAsync(IPage page, string prefix = "edit")
    {
        var email = NewEmail(prefix);
        await RegisterAsync(page, email, StrongPassword);
        return (await GetUserAsync(fixture.App, email))!.Id;
    }

    /// <summary>
    /// Waits until the page has an interactive circuit. After a client-side navigation it is already
    /// true; after a full page load (the stale notice's Reload) it is true again once the new circuit
    /// has connected, which is what the form's inputs need before they can be used.
    /// </summary>
    private static async Task WaitForReadyAsync(IPage page) =>
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

    private static async Task AddRowAsync(IPage page) =>
        await page.Locator("[data-testid='person-contact-method-add']").ClickAsync();

    private static async Task ChooseKindAsync(ILocator row, IPage page, string kind)
    {
        await row.Locator("[data-testid='person-contact-method-kind-field'] .mud-input-control").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = kind, Exact = true }).ClickAsync();
    }

    /// <summary>Types a label and picks the suggestion, the way a person would.</summary>
    private static async Task ChooseLabelAsync(ILocator row, IPage page, string label)
    {
        await row.Locator("[data-testid='person-contact-method-label-field'] input").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = label, Exact = true }).ClickAsync();
    }

    private static async Task CreateTagByTypingAsync(IPage page, string name)
    {
        await TagField(page).FillAsync(name);
        await Options(page).Filter(new() { HasTextString = "Create tag" }).ClickAsync();
    }

    private static async Task SaveAsync(IPage page) =>
        await page.Locator("[data-testid='person-form-save']").ClickAsync();

    [Fact]
    public async Task Contact_methods_and_tags_can_be_added_edited_and_removed()
    {
        var firstName = NewFirstName();
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);

        // Create with an email (labelled Work) and a brand new tag.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByLabel("First name").FillAsync(firstName);
        await AddRowAsync(page);
        await ValueOf(Rows(page).First).FillAsync("ada@example.com");
        await ChooseLabelAsync(Rows(page).First, page, "Work");
        await CreateTagByTypingAsync(page, "Climbing");
        await Expect(Chips(page)).ToHaveCountAsync(1);
        await SaveAsync(page);

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(Headings(page)).ToHaveTextAsync(["Email · Work"]);
        var emailLink = page.Locator("[data-testid='person-contact-method-link']");
        await Expect(emailLink).ToHaveAttributeAsync("href", "mailto:ada@example.com");
        await Expect(page.Locator("[data-testid='person-tag']")).ToHaveTextAsync(["Climbing"]);

        // Edit: change the email, add a phone, add a tag with Enter, remove the old tag.
        await page.Locator("[data-testid='person-edit']").ClickAsync();
        await Expect(page).ToHaveURLAsync(EditUrl);
        await WaitForReadyAsync(page);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Edit details", Exact = true })).ToBeVisibleAsync();
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await Expect(ValueOf(Rows(page).First)).ToHaveValueAsync("ada@example.com");
        await Expect(Chips(page)).ToHaveTextAsync(["Climbing"]);

        await ValueOf(Rows(page).First).FillAsync("ada.l@example.com");
        await AddRowAsync(page);
        var phoneRow = Rows(page).Nth(1);
        await ChooseKindAsync(phoneRow, page, "Phone");
        await ValueOf(phoneRow).FillAsync("+44 7700 900123");
        await ChooseLabelAsync(phoneRow, page, "Mobile");

        await TagField(page).FillAsync("Chess");
        await Options(page).Filter(new() { HasTextString = "Create tag" }).WaitForAsync();
        await TagField(page).PressAsync("Enter");
        await Expect(Chips(page)).ToHaveTextAsync(["Climbing", "Chess"]);
        await Expect(page).ToHaveURLAsync(EditUrl); // Enter chose the tag; it did not submit the form.

        await page.GetByRole(AriaRole.Button, new() { Name = "Remove tag Climbing", Exact = true }).ClickAsync();
        await Expect(Chips(page)).ToHaveTextAsync(["Chess"]);
        await SaveAsync(page);

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(Headings(page)).ToHaveTextAsync(["Email · Work", "Phone · Mobile"]);
        await Expect(page.Locator("[data-testid='person-contact-method-link']").First)
            .ToHaveAttributeAsync("href", "mailto:ada.l@example.com");
        await Expect(page.Locator("[data-testid='person-contact-method-link']").Nth(1))
            .ToHaveAttributeAsync("href", "tel:+447700900123");
        await Expect(page.Locator("[data-testid='person-tag']")).ToHaveTextAsync(["Chess"]);
        await Expect(page.Locator(".mud-snackbar", new() { HasText = "Changes saved" })).ToBeVisibleAsync();

        // Edit again: remove the email row.
        await page.Locator("[data-testid='person-edit']").ClickAsync();
        await WaitForReadyAsync(page);
        await Expect(Rows(page)).ToHaveCountAsync(2);
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove contact method 1", Exact = true }).ClickAsync();
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await SaveAsync(page);

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(Headings(page)).ToHaveTextAsync(["Phone · Mobile"]);

        // A second person finds the tags that exist, and typing a known name offers no duplicate.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByLabel("First name").FillAsync(NewFirstName());
        await TagField(page).FillAsync("chess");
        await Expect(Options(page).Filter(new() { HasTextString = "Chess" })).ToHaveCountAsync(1);
        await Expect(Options(page).Filter(new() { HasTextString = "Create tag" })).ToHaveCountAsync(0);

        (await PeopleTestHelpers.TagNamesAsync(fixture.App, ownerId)).Should().Equal("Chess", "Climbing");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Editing_the_name_and_relationship_type_is_reflected_in_the_profile_and_the_list()
    {
        var firstName = NewFirstName();
        var newFirstName = NewFirstName();
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var personId = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = firstName });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}/edit");
        await Expect(page.GetByLabel("First name")).ToHaveValueAsync(firstName);
        await page.GetByLabel("First name").FillAsync(newFirstName);
        await page.GetByLabel("Last name").FillAsync("Lovelace");
        await page.Locator("[data-testid='person-relationship-field'] .mud-input-control").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Friend", Exact = true }).ClickAsync();
        await SaveAsync(page);

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync($"{newFirstName} Lovelace");
        await Expect(page.Locator("[data-testid='person-relationship']")).ToHaveTextAsync("Friend");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        var row = page.Locator("[data-testid='people-list-item']", new() { HasText = newFirstName });
        await Expect(row.Locator("[data-testid='people-list-item-relationship']")).ToHaveTextAsync("Friend");
        await Expect(page.Locator("[data-testid='people-list-item']", new() { HasText = firstName })).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Invalid_contact_details_are_explained_and_nothing_is_saved()
    {
        var firstName = NewFirstName();
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var personId = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = firstName });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}/edit");
        await AddRowAsync(page);
        await AddRowAsync(page);
        await ValueOf(Rows(page).First).FillAsync("not an email");
        var second = Rows(page).Nth(1);
        await ChooseKindAsync(second, page, "Phone");
        await ValueOf(second).FillAsync("abc");
        await SaveAsync(page);

        await Expect(Rows(page).First).ToContainTextAsync("Enter an email address like name@example.com.");
        await Expect(second).ToContainTextAsync("Use digits, spaces, a leading + and ( ) - . / only.");
        await Expect(page).ToHaveURLAsync(EditUrl);

        await ValueOf(second).FillAsync("12");
        await SaveAsync(page);
        await Expect(second).ToContainTextAsync("Enter a phone number with 3 to 15 digits.");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}");
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync(firstName);
        await Expect(page.Locator("[data-testid='person-contact-method']")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Editing_a_person_works_on_a_phone()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        var ownerId = await RegisterFreshUserAsync(page);
        var personId = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = NewFirstName() });
        await PeopleTestHelpers.CreatePersonAsync(
            fixture.App, ownerId, new CreatePersonRequest { FirstName = NewFirstName(), NewTagNames = ["Chess"] });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}/edit");
        await AddRowAsync(page);
        var row = Rows(page).First;
        await ChooseKindAsync(row, page, "Phone");
        await ValueOf(row).FillAsync("+44 7700 900123");

        await Expect(ValueOf(row)).ToHaveAttributeAsync("type", "tel"); // a phone keyboard opens for a phone number
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the form must not scroll sideways on a phone");

        var remove = row.Locator("[data-testid='person-contact-method-remove']");
        (await remove.BoundingBoxAsync())!.Height.Should().BeGreaterThanOrEqualTo(44, "a touch target is at least 44px");

        await TagField(page).ClickAsync();
        await Options(page).Filter(new() { HasTextString = "Chess" }).First.ClickAsync();
        await Expect(Chips(page)).ToHaveTextAsync(["Chess"]);

        var save = page.Locator("[data-testid='person-form-save']");
        await save.ScrollIntoViewIfNeededAsync();
        (await save.BoundingBoxAsync())!.Width.Should().BeGreaterThan((float)(Viewports.Phone.Width * 0.8), "the primary action is a full-width button");
        await save.ClickAsync();

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(Headings(page)).ToHaveTextAsync(["Phone"]);
        await Expect(page.Locator("[data-testid='person-tag']")).ToHaveTextAsync(["Chess"]);
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the profile must not scroll sideways on a phone");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_contact_method_removed_in_another_tab_is_reported_by_the_stale_tab()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var personId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = NewFirstName(),
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Phone, null, "+44 7700 900123"),
            ],
        });

        // Tab one opens the form and leaves it.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}/edit");
        await Expect(Rows(page)).ToHaveCountAsync(2);

        // Tab two (same browser, same session) removes the email and saves.
        var other = await page.Context.NewPageAsync();
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(other, $"/people/{personId}/edit");
        await Expect(Rows(other)).ToHaveCountAsync(2);
        await other.GetByRole(AriaRole.Button, new() { Name = "Remove contact method 1", Exact = true }).ClickAsync();
        await SaveAsync(other);
        await Expect(other).ToHaveURLAsync(ProfileUrl);
        await other.CloseAsync();

        // Tab one still shows both rows. Saving must not quietly bring the email back.
        await ValueOf(Rows(page).Nth(1)).FillAsync("+44 7700 900999");
        await SaveAsync(page);

        var stale = page.Locator("[data-testid='person-form-stale']");
        await Expect(stale).ToContainTextAsync("This profile changed in another tab or window.");
        await Expect(page).ToHaveURLAsync(EditUrl);

        await page.Locator("[data-testid='person-form-reload']").ClickAsync();
        await WaitForReadyAsync(page);
        await Expect(Rows(page)).ToHaveCountAsync(1);
        await Expect(ValueOf(Rows(page).First)).ToHaveValueAsync("+44 7700 900123");
        await Expect(stale).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Another_users_person_edit_link_shows_not_found()
    {
        var firstName = NewFirstName();
        var pageA = await fixture.NewPageAsync();
        var ownerA = await RegisterFreshUserAsync(pageA, "owner");
        var personId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerA, new CreatePersonRequest
        {
            FirstName = firstName,
            LastName = "Private",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "private@example.com")],
        });
        await RelioAppFixture.ClosePageAsync(pageA);

        var pageB = await fixture.NewPageAsync();
        await RegisterFreshUserAsync(pageB, "intruder");
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, $"/people/{personId}/edit");

        await Expect(pageB.Locator("[data-testid='person-not-found']")).ToBeVisibleAsync();
        await Expect(pageB.Locator("[data-testid='person-form']")).ToHaveCountAsync(0);
        var content = await pageB.ContentAsync();
        content.Should().NotContain(firstName).And.NotContain("Private").And.NotContain("private@example.com");

        // A person that never existed looks exactly the same.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, $"/people/{Guid.NewGuid()}/edit");
        await Expect(pageB.Locator("[data-testid='person-not-found']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(pageB);
    }

    [Fact]
    public async Task A_person_added_with_contact_details_shows_them_on_the_profile_and_the_page_title_stays_generic()
    {
        var firstName = NewFirstName();
        var page = await fixture.NewPageAsync();
        await RegisterFreshUserAsync(page);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByLabel("First name").FillAsync(firstName);
        await AddRowAsync(page);
        var row = Rows(page).First;
        await ChooseKindAsync(row, page, "Social");
        await ValueOf(row).FillAsync("javascript:alert(1)");
        // A label nobody suggested, typed and then straight on to Save: the box must keep what was typed.
        await row.Locator("[data-testid='person-contact-method-label-field'] input").FillAsync("Mum's page");
        await SaveAsync(page);

        await Expect(page).ToHaveURLAsync(ProfileUrl);
        await Expect(Headings(page)).ToHaveTextAsync(["Social · Mum's page"]);
        await Expect(page.Locator("[data-testid='person-contact-method-value']")).ToHaveTextAsync("javascript:alert(1)");
        await Expect(page.Locator("a[href^='javascript']")).ToHaveCountAsync(0);

        // The head is server-rendered, so a title is only read on a full page load: load both pages
        // afresh. Neither carries the person's name.
        var profilePath = new Uri(page.Url).AbsolutePath;
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, profilePath);
        await Expect(page).ToHaveTitleAsync("Person - Relio");
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"{profilePath}/edit");
        await Expect(page).ToHaveTitleAsync("Edit person - Relio");

        await RelioAppFixture.ClosePageAsync(page);
    }
}
