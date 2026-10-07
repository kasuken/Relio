using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data;
using Relio.Data.People;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #26 end to end: archiving and restoring a person from the profile's More menu and the
/// archived note, and deleting them for good through the confirmation, in a real browser over a real
/// circuit. Every test registers its own fresh user and uses unique names - the shared demo user's
/// people are never archived, restored or deleted, because every other test reads them.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class PersonArchiveDeleteTests(RelioAppFixture fixture)
{
    private static readonly Regex ListUrl = new("/people$");

    private static readonly Regex ProfileUrl =
        new("/people/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

    private static string NewFirstName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..16];

    private async Task<string> RegisterFreshUserAsync(IPage page, string prefix)
    {
        var email = NewEmail(prefix);
        await RegisterAsync(page, email, StrongPassword);
        return (await GetUserAsync(fixture.App, email))!.Id;
    }

    private static ILocator Snackbar(IPage page, string text) =>
        page.Locator(".mud-snackbar", new() { HasText = text });

    private static async Task OpenMenuAsync(IPage page) =>
        await page.Locator("[data-testid='person-actions']")
            .GetByRole(AriaRole.Button, new() { Name = "More", Exact = true })
            .ClickAsync();

    private static async Task ChooseAsync(IPage page, string testId)
    {
        await OpenMenuAsync(page);
        await page.Locator($"[data-testid='{testId}']").ClickAsync();
    }

    private static PeopleService ServiceFor(IServiceScope scope, string ownerId) =>
        new(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new FixedCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());

    /// <summary>What is left in the database for a person, read straight from it.</summary>
    private async Task<(bool PersonExists, int ContactMethods, int TagLinks)> RowsForAsync(Guid personId)
    {
        using var scope = fixture.App.CreateRealScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        var person = await dbContext.People.AsNoTracking().AnyAsync(p => p.Id == personId);
        var contactMethods = await dbContext.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == personId);
        var links = await dbContext.Set<Dictionary<string, object>>("PersonTag").AsNoTracking().ToListAsync();
        return (person, contactMethods, links.Count(link => (Guid)link["PeopleId"] == personId));
    }

    private static CreatePersonRequest Request(string firstName) => new()
    {
        FirstName = firstName,
        LastName = "Lovelace",
        NewTagNames = ["Chess"],
        ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")],
    };

    [Fact]
    public async Task Archiving_hides_a_person_from_the_list_until_show_archived_is_on_and_restoring_brings_them_back()
    {
        var ada = NewFirstName("Ada");
        var bea = NewFirstName("Bea");
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page, "archive");
        await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = ada });
        await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = bea });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(page.Locator("[data-testid='people-count']")).ToBeVisibleAsync();
        var names = page.Locator("[data-testid='people-list-item-name']");
        await Expect(names).ToHaveCountAsync(2);
        await page.Locator("[data-testid='people-list-item']", new() { HasText = ada }).ClickAsync();
        await Expect(page).ToHaveURLAsync(ProfileUrl);

        await ChooseAsync(page, "person-archive");

        await Expect(page.Locator("[data-testid='person-archived']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='person-archived-date']")).ToContainTextAsync("Archived on");
        await Expect(Snackbar(page, "Person archived")).ToBeVisibleAsync();
        await Expect(Snackbar(page, "Person archived")).Not.ToContainTextAsync(ada);
        await Expect(page).ToHaveURLAsync(ProfileUrl); // stays on the profile
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveCountAsync(1);

        // The default list no longer shows them; "Show archived" does, labelled.
        await page.Locator("[data-testid='person-back']").ClickAsync();
        await Expect(page).ToHaveURLAsync(ListUrl);
        await Expect(page.Locator("[data-testid='people-count']")).ToBeVisibleAsync();
        await Expect(names.Filter(new() { HasTextString = bea })).ToHaveCountAsync(1);
        await Expect(names.Filter(new() { HasTextString = ada })).ToHaveCountAsync(0);

        await page.Locator("[data-testid='people-show-archived'] input").ClickAsync();
        var adaRow = page.Locator("[data-testid='people-list-item']", new() { HasText = ada });
        await Expect(adaRow).ToBeVisibleAsync();
        await Expect(adaRow.Locator("[data-testid='people-list-item-archived']")).ToHaveTextAsync("Archived");

        // Restore from the note on the profile: it comes back, with its details.
        await adaRow.ClickAsync();
        await Expect(page.Locator("[data-testid='person-archived']")).ToBeVisibleAsync();
        await page.Locator("[data-testid='person-restore']").ClickAsync();
        await Expect(page.Locator("[data-testid='person-archived']")).ToHaveCountAsync(0);
        await Expect(Snackbar(page, "Person restored")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='person-name']")).ToContainTextAsync(ada);

        await page.Locator("[data-testid='person-back']").ClickAsync();
        await Expect(page.Locator("[data-testid='people-count']")).ToBeVisibleAsync();
        await Expect(names.Filter(new() { HasTextString = ada })).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid='people-list-item-archived']")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Deleting_a_person_after_confirming_removes_them_and_everything_about_them()
    {
        var ada = NewFirstName("Ada");
        var bea = NewFirstName("Bea");
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page, "delete");
        var adaId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, Request(ada));
        await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = bea });
        (await RowsForAsync(adaId)).Should().Be((true, 1, 1));

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(page.Locator("[data-testid='people-count']")).ToBeVisibleAsync();
        await page.Locator("[data-testid='people-list-item']", new() { HasText = ada }).ClickAsync();
        await Expect(page).ToHaveURLAsync(ProfileUrl);
        var profileUrl = page.Url;

        await ChooseAsync(page, "person-delete");
        var dialog = page.Locator(".mud-dialog");
        await Expect(dialog).ToContainTextAsync($"Delete {ada} Lovelace?");
        await Expect(dialog).ToContainTextAsync("It can't be undone.");
        await Expect(page.Locator("[data-testid='confirm-dialog-confirm']")).ToHaveTextAsync("Delete permanently");
        (await RowsForAsync(adaId)).PersonExists.Should().BeTrue("nothing is deleted before the confirmation");
        await page.Locator("[data-testid='confirm-dialog-confirm']").ClickAsync();

        await Expect(page).ToHaveURLAsync(ListUrl);
        await Expect(Snackbar(page, "Person deleted")).ToBeVisibleAsync();
        await Expect(Snackbar(page, "Person deleted")).Not.ToContainTextAsync(ada);
        await Expect(page.Locator("[data-testid='people-count']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='people-list-item-name']").Filter(new() { HasTextString = ada })).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='people-list-item-name']").Filter(new() { HasTextString = bea })).ToHaveCountAsync(1);

        // Everything about them is gone from the database - and the tag they had is not.
        (await RowsForAsync(adaId)).Should().Be((false, 0, 0));
        (await PeopleTestHelpers.TagNamesAsync(fixture.App, ownerId)).Should().Equal("Chess");

        // The profile was replaced in the history, so Back never lands on it.
        await page.GoBackAsync();
        await Expect(page).Not.ToHaveURLAsync(ProfileUrl);

        // And its address now shows the same panel as any person who isn't there.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, new Uri(profileUrl).AbsolutePath);
        await Expect(page.Locator("[data-testid='person-not-found']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Cancelling_the_delete_keeps_the_person()
    {
        var ada = NewFirstName("Ada");
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page, "cancel");
        var adaId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, Request(ada));
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{adaId}");
        await Expect(page.Locator("[data-testid='person-name']")).ToContainTextAsync(ada);

        // Cancel.
        await ChooseAsync(page, "person-delete");
        await Expect(page.Locator(".mud-dialog")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='confirm-dialog-confirm']")).Not.ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter"); // an Enter on opening must never delete.
        await Expect(page.Locator(".mud-dialog")).ToBeVisibleAsync();
        (await RowsForAsync(adaId)).PersonExists.Should().BeTrue();
        await page.Locator("[data-testid='confirm-dialog-cancel']").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='person-name']")).ToContainTextAsync(ada);

        // Escape.
        await ChooseAsync(page, "person-delete");
        await Expect(page.Locator(".mud-dialog")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='person-name']")).ToContainTextAsync(ada);

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{adaId}$"));
        await Expect(page.Locator(".mud-snackbar", new() { HasText = "Person deleted" })).ToHaveCountAsync(0);
        (await RowsForAsync(adaId)).Should().Be((true, 1, 1));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Another_users_person_cannot_be_archived_or_deleted()
    {
        var ada = NewFirstName("Ada");
        var owner = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(owner, "owner");
        var adaId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, Request(ada));
        await RelioAppFixture.ClosePageAsync(owner);

        var intruder = await fixture.NewPageAsync();
        var intruderId = await RegisterFreshUserAsync(intruder, "intruder");

        // Through the page: the identical "isn't in your list" panel, and nothing to act on.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(intruder, $"/people/{adaId}");
        await Expect(intruder.Locator("[data-testid='person-not-found']")).ToBeVisibleAsync();
        await Expect(intruder.Locator("[data-testid='person-actions']")).ToHaveCountAsync(0);

        // Through the service, as the intruder: no result, and nothing changes.
        using (var scope = fixture.App.CreateRealScope())
        {
            var service = ServiceFor(scope, intruderId);
            (await service.ArchiveAsync(adaId)).Should().BeFalse();
            (await service.RestoreAsync(adaId)).Should().BeFalse();
            (await service.DeleteAsync(adaId)).Should().BeFalse();
        }

        (await RowsForAsync(adaId)).Should().Be((true, 1, 1));
        using (var scope = fixture.App.CreateRealScope())
        {
            var stillActive = await ServiceFor(scope, ownerId).GetAsync(adaId);
            stillActive!.IsArchived.Should().BeFalse();
        }

        await RelioAppFixture.ClosePageAsync(intruder);
    }

    [Fact]
    public async Task Archive_restore_and_delete_work_on_a_phone()
    {
        var ada = NewFirstName("Ada");
        var page = await fixture.NewPageAsync(Viewports.Phone);
        var ownerId = await RegisterFreshUserAsync(page, "phone");
        var adaId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, Request(ada));
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{adaId}");

        // Archive: the note appears and the page does not scroll sideways.
        await ChooseAsync(page, "person-archive");
        await Expect(page.Locator("[data-testid='person-archived']")).ToBeVisibleAsync();
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the archived note fits a phone");
        var restore = await page.Locator("[data-testid='person-restore']").BoundingBoxAsync();
        restore!.Height.Should().BeGreaterThanOrEqualTo(44, "the Restore button is a 44px target");
        var more = await page.Locator("[data-testid='person-actions']")
            .GetByRole(AriaRole.Button, new() { Name = "More", Exact = true }).BoundingBoxAsync();
        more!.Height.Should().BeGreaterThanOrEqualTo(44, "the More button is a 44px target");

        // Restore from the note.
        await page.Locator("[data-testid='person-restore']").ClickAsync();
        await Expect(page.Locator("[data-testid='person-archived']")).ToHaveCountAsync(0);

        // Delete: the dialog fits the viewport, and deleting works.
        await ChooseAsync(page, "person-delete");
        var dialog = page.Locator(".mud-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        var box = await dialog.BoundingBoxAsync();
        box!.X.Should().BeGreaterThanOrEqualTo(0);
        (box.X + box.Width).Should().BeLessThanOrEqualTo(Viewports.Phone.Width);
        await page.Locator("[data-testid='confirm-dialog-confirm']").ClickAsync();

        await Expect(page).ToHaveURLAsync(ListUrl);
        (await RowsForAsync(adaId)).PersonExists.Should().BeFalse();

        await RelioAppFixture.ClosePageAsync(page);
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }
}
