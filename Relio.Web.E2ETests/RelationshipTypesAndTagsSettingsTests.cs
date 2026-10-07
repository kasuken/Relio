using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #25 end to end: managing relationship types and tags in settings, in a real browser over a
/// real circuit, and reading the effect back on real profiles. Every test registers its own fresh
/// user (and so its own labels) and never changes the shared demo user's relationship types or tags.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class RelationshipTypesAndTagsSettingsTests(RelioAppFixture fixture)
{
    private static readonly string[] DefaultTypeNames = ["Family", "Partner", "Friend", "Colleague", "Acquaintance", "Other"];

    private static string NewName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private async Task<string> RegisterFreshUserAsync(IPage page)
    {
        var email = NewEmail("labels");
        await RegisterAsync(page, email, StrongPassword);
        return (await GetUserAsync(fixture.App, email))!.Id;
    }

    private static ILocator TypeRow(IPage page, string name) =>
        page.Locator("[data-testid='relationship-type-row']", new() { HasText = name });

    private static ILocator TagRow(IPage page, string name) =>
        page.Locator("[data-testid='tag-row']", new() { HasText = name });

    private static ILocator Snackbar(IPage page, string text) =>
        page.Locator(".mud-snackbar", new() { HasText = text });

    private async Task<Guid> AddPersonAsync(string ownerId, string firstName, Guid? typeId = null, params string[] tags) =>
        await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = firstName,
            RelationshipTypeId = typeId,
            NewTagNames = tags,
        });

    private async Task OpenProfileAsync(IPage page, Guid personId)
    {
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}");
        await Expect(page.GetByTestId("person-name")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_new_user_starts_with_the_six_default_relationship_types()
    {
        var page = await fixture.NewPageAsync();
        await RegisterFreshUserAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Relationship types and tags", Exact = true })).ToBeVisibleAsync();
        await page.GetByTestId("settings-relationship-types-link").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/settings/relationship-types$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Relationship types", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByTestId("relationship-type-row-name")).ToHaveTextAsync(DefaultTypeNames);
        await Expect(page.GetByTestId("relationship-type-row-count")).ToHaveTextAsync(Enumerable.Repeat("No one yet", 6).ToArray());

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Renaming_a_tag_updates_every_person_using_it()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var ada = await AddPersonAsync(ownerId, NewName("Ada"), null, "Climbing");
        var bo = await AddPersonAsync(ownerId, NewName("Bo"), null, "Climbing");
        var cy = await AddPersonAsync(ownerId, NewName("Cy"));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/tags");
        await Expect(TagRow(page, "Climbing").GetByTestId("tag-row-count")).ToHaveTextAsync("2 people");
        await TagRow(page, "Climbing").GetByTestId("tag-rename").ClickAsync();
        await Expect(page.GetByTestId("label-name-dialog-field").Locator("input")).ToHaveValueAsync("Climbing");
        await page.GetByTestId("label-name-dialog-field").Locator("input").FillAsync("Bouldering");
        await page.GetByTestId("label-name-dialog-save").ClickAsync();

        await Expect(Snackbar(page, "Tag renamed")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("tag-row-name")).ToHaveTextAsync(["Bouldering"]);

        await OpenProfileAsync(page, ada);
        await Expect(page.GetByTestId("person-tag")).ToHaveTextAsync(["Bouldering"]);
        await OpenProfileAsync(page, bo);
        await Expect(page.GetByTestId("person-tag")).ToHaveTextAsync(["Bouldering"]);
        await OpenProfileAsync(page, cy);
        await Expect(page.GetByTestId("person-tag")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Removing_a_relationship_type_in_use_moves_its_people_to_another_type()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var friend = await PeopleTestHelpers.RelationshipTypeIdAsync(fixture.App, ownerId, "Friend");
        var ada = await AddPersonAsync(ownerId, NewName("Ada"), friend);
        var bo = await AddPersonAsync(ownerId, NewName("Bo"), friend);
        await PeopleTestHelpers.ArchivePersonAsync(fixture.App, ownerId, bo);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/relationship-types");
        await Expect(TypeRow(page, "Friend").GetByTestId("relationship-type-row-count")).ToHaveTextAsync("2 people");
        await TypeRow(page, "Friend").GetByTestId("relationship-type-remove").ClickAsync();
        await Expect(page.GetByTestId("remove-type-dialog-message")).ToContainTextAsync("2 people have this relationship type.");
        await Expect(page.GetByTestId("remove-type-dialog-confirm")).ToBeDisabledAsync();
        await page.GetByTestId("remove-type-dialog-target-field").Locator(".mud-input-control").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Acquaintance", Exact = true }).ClickAsync();
        await page.GetByTestId("remove-type-dialog-confirm").ClickAsync();

        await Expect(Snackbar(page, "Relationship type removed")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("relationship-type-row-name")).ToHaveTextAsync(
            DefaultTypeNames.Where(n => n != "Friend").ToArray());
        await Expect(TypeRow(page, "Acquaintance").GetByTestId("relationship-type-row-count")).ToHaveTextAsync("2 people");

        await OpenProfileAsync(page, ada);
        await Expect(page.GetByTestId("person-relationship")).ToHaveTextAsync("Acquaintance");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people/new");
        await page.GetByTestId("person-relationship-field").Locator(".mud-input-control").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Option)).ToHaveTextAsync(
            DefaultTypeNames.Where(n => n != "Friend").ToArray());

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Removing_a_relationship_type_can_leave_people_without_one()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var colleague = await PeopleTestHelpers.RelationshipTypeIdAsync(fixture.App, ownerId, "Colleague");
        var ada = await AddPersonAsync(ownerId, NewName("Ada"), colleague);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/relationship-types");
        await TypeRow(page, "Colleague").GetByTestId("relationship-type-remove").ClickAsync();
        await Expect(page.GetByTestId("remove-type-dialog-message")).ToContainTextAsync("1 person has this relationship type.");
        await page.GetByRole(AriaRole.Radio, new() { Name = "Leave their relationship type empty" }).CheckAsync();
        await Expect(page.GetByTestId("remove-type-dialog-confirm")).ToBeEnabledAsync();
        await page.GetByTestId("remove-type-dialog-confirm").ClickAsync();

        await Expect(Snackbar(page, "Relationship type removed")).ToBeVisibleAsync();
        await Expect(TypeRow(page, "Colleague")).ToHaveCountAsync(0);

        await OpenProfileAsync(page, ada);
        await Expect(page.GetByTestId("person-relationship")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Removing_a_tag_takes_it_off_people()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var ada = await AddPersonAsync(ownerId, NewName("Ada"), null, "Climbing", "Chess");
        var bo = await AddPersonAsync(ownerId, NewName("Bo"), null, "Climbing");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/tags");
        await TagRow(page, "Climbing").GetByTestId("tag-remove").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToContainTextAsync("2 people have this tag.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove tag", Exact = true }).ClickAsync();

        await Expect(Snackbar(page, "Tag removed")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("tag-row-name")).ToHaveTextAsync(["Chess"]);

        await OpenProfileAsync(page, ada);
        await Expect(page.GetByTestId("person-tag")).ToHaveTextAsync(["Chess"]);
        await OpenProfileAsync(page, bo);
        await Expect(page.GetByTestId("person-tag")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_name_that_differs_only_in_case_is_refused()
    {
        var page = await fixture.NewPageAsync();
        await RegisterFreshUserAsync(page);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/relationship-types");
        await page.GetByTestId("relationship-type-add-field").Locator("input").FillAsync("fRIEND");
        await page.GetByTestId("relationship-type-add-field").Locator("input").PressAsync("Enter");
        await Expect(page.GetByTestId("relationship-type-add-field"))
            .ToContainTextAsync("You already have a relationship type with that name.");
        await Expect(page.GetByTestId("relationship-type-row")).ToHaveCountAsync(6);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/tags");
        await Expect(page.GetByTestId("tags-empty")).ToBeVisibleAsync();
        await page.GetByTestId("tag-add-field").Locator("input").FillAsync("Climbing");
        await page.GetByTestId("tag-add-field").Locator("input").PressAsync("Enter");
        await Expect(Snackbar(page, "Tag added")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("tag-row-name")).ToHaveTextAsync(["Climbing"]);

        await page.GetByTestId("tag-add-field").Locator("input").FillAsync("climbing");
        await page.GetByTestId("tag-add-field").Locator("input").PressAsync("Enter");
        await Expect(page.GetByTestId("tag-add-field")).ToContainTextAsync("You already have a tag with that name.");
        await Expect(page.GetByTestId("tag-row")).ToHaveCountAsync(1);

        // Renaming a tag to another one's name, in another case, is refused inside the dialog.
        await page.GetByTestId("tag-add-field").Locator("input").FillAsync("Chess");
        await page.GetByTestId("tag-add-field").Locator("input").PressAsync("Enter");
        await Expect(page.GetByTestId("tag-row")).ToHaveCountAsync(2);
        await TagRow(page, "Chess").GetByTestId("tag-rename").ClickAsync();
        await page.GetByTestId("label-name-dialog-field").Locator("input").FillAsync("CLIMBING");
        await page.GetByTestId("label-name-dialog-save").ClickAsync();
        await Expect(page.GetByTestId("label-name-dialog-field")).ToContainTextAsync("You already have a tag with that name.");
        await page.GetByTestId("label-name-dialog-cancel").ClickAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Managing_relationship_types_and_tags_works_on_a_phone()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        await RegisterFreshUserAsync(page);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/relationship-types");
        await Expect(page.GetByTestId("relationship-type-row")).ToHaveCountAsync(6);
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page does not scroll sideways");

        foreach (var testId in new[] { "relationship-type-rename", "relationship-type-remove" })
        {
            var box = await page.GetByTestId(testId).First.BoundingBoxAsync();
            box!.Width.Should().BeGreaterThanOrEqualTo(44);
            box.Height.Should().BeGreaterThanOrEqualTo(44);
        }

        var field = await page.GetByTestId("relationship-type-add-field").BoundingBoxAsync();
        var add = await page.GetByTestId("relationship-type-add").BoundingBoxAsync();
        add!.Width.Should().BeGreaterThanOrEqualTo(field!.Width - 1, "the add button is full width on a phone");
        add.Y.Should().BeGreaterThan(field.Y, "and sits under the field");

        await page.GetByTestId("relationship-type-add-field").Locator("input").FillAsync("Mentor");
        await page.GetByTestId("relationship-type-add").ClickAsync();
        await Expect(TypeRow(page, "Mentor")).ToBeVisibleAsync();

        await TypeRow(page, "Mentor").GetByTestId("relationship-type-rename").ClickAsync();
        var dialog = await page.Locator(".mud-dialog").BoundingBoxAsync();
        dialog!.X.Should().BeGreaterThanOrEqualTo(0);
        (dialog.X + dialog.Width).Should().BeLessThanOrEqualTo(Viewports.Phone.Width);
        await page.GetByTestId("label-name-dialog-field").Locator("input").FillAsync("Coach");
        await page.GetByTestId("label-name-dialog-save").ClickAsync();
        await Expect(TypeRow(page, "Coach")).ToBeVisibleAsync();

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings/tags");
        await page.GetByTestId("tag-add-field").Locator("input").FillAsync("Climbing");
        await page.GetByTestId("tag-add").ClickAsync();
        await Expect(TagRow(page, "Climbing")).ToBeVisibleAsync();
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the tags page does not scroll sideways either");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Another_users_types_and_tags_never_appear()
    {
        var pageA = await fixture.NewPageAsync();
        var ownerA = await RegisterFreshUserAsync(pageA);
        var secretType = NewName("Mentor");
        var secretTag = NewName("Secret");
        await PeopleTestHelpers.CreateRelationshipTypeAsync(fixture.App, ownerA, secretType);
        await AddPersonAsync(ownerA, NewName("Ada"), null, secretTag);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageA, "/settings/relationship-types");
        await Expect(TypeRow(pageA, secretType)).ToHaveCountAsync(1);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageA, "/settings/tags");
        await Expect(TagRow(pageA, secretTag).GetByTestId("tag-row-count")).ToHaveTextAsync("1 person");

        var pageB = await fixture.NewPageAsync();
        await RegisterFreshUserAsync(pageB);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, "/settings/relationship-types");
        await Expect(pageB.GetByTestId("relationship-type-row-name")).ToHaveTextAsync(DefaultTypeNames);
        await Expect(pageB.GetByTestId("relationship-type-row-count")).ToHaveTextAsync(Enumerable.Repeat("No one yet", 6).ToArray());
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, "/settings/tags");
        await Expect(pageB.GetByTestId("tags-empty")).ToBeVisibleAsync();
        (await pageB.ContentAsync()).Should().NotContain(secretTag).And.NotContain(secretType);

        await RelioAppFixture.ClosePageAsync(pageA);
        await RelioAppFixture.ClosePageAsync(pageB);
    }
}
