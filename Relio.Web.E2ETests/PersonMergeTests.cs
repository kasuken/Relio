using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.Interactions;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #28 end to end: merging two profiles of the same person from the profile's More menu or the
/// duplicate warning, in a real browser over a real circuit. Every test registers its own fresh user and
/// never touches the shared demo user's people. The merged profile can only be proven to hold the contact
/// methods, tags, details and both non-repeated and repeated shared interactions.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class PersonMergeTests(RelioAppFixture fixture)
{
    private const string GuidPattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    private static ILocator Field(IPage page, string field) =>
        page.Locator($"[data-testid='merge-field'][data-field='{field}']");

    private async Task<string> RegisterFreshUserAsync(IPage page)
    {
        var email = NewEmail("merge");
        await RegisterAsync(page, email, StrongPassword);
        return (await GetUserAsync(fixture.App, email))!.Id;
    }

    /// <summary>John Smith (an email, the Chess tag, details) and Jon Smith (a phone, the same email in capitals, the Climbing tag, details).</summary>
    private async Task<(Guid John, Guid Jon)> SeedJohnAndJonAsync(string ownerId)
    {
        var john = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = "John",
            LastName = "Smith",
            Details = "Met at the chess club.",
            NewTagNames = ["Chess"],
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "john@example.com")],
        });
        var jon = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = "Jon",
            LastName = "Smith",
            Details = "Climbs on Tuesdays.",
            NewTagNames = ["Climbing"],
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Phone, "Mobile", "+44 7700 900123"),
                new ContactMethodInput(null, ContactMethodKind.Email, "Work", "JOHN@example.com"),
            ],
        });
        return (john, jon);
    }

    private static async Task ConfirmMergeAsync(IPage page)
    {
        await page.Locator("[data-testid='merge-submit']").ClickAsync();
        await page.Locator("[data-testid='confirm-dialog-confirm']").ClickAsync();
    }

    [Fact]
    public async Task Merging_two_profiles_leaves_one_profile_with_everything_from_both()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var (john, jon) = await SeedJohnAndJonAsync(ownerId);
        var occurredOn = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(-1);
        var sharedInteractionId = await PeopleTestHelpers.CreateInteractionAsync(fixture.App, ownerId, new CreateInteractionRequest
        {
            ProfilePersonId = john,
            OccurredOn = occurredOn,
            Kind = InteractionKind.Meeting,
            Description = "We visited the museum together.",
            ParticipantIds = [john, jon],
        });
        var duplicateOnlyInteractionId = await PeopleTestHelpers.CreateInteractionAsync(fixture.App, ownerId, new CreateInteractionRequest
        {
            ProfilePersonId = jon,
            OccurredOn = occurredOn,
            Kind = InteractionKind.Message,
            Description = "Jon sent the gallery opening details.",
            ParticipantIds = [jon],
        });
        await PeopleTestHelpers.CreateNoteAsync(
            fixture.App, ownerId, john, "Remember John's observatory story.", isPinned: true);
        await PeopleTestHelpers.CreateNoteAsync(
            fixture.App, ownerId, jon, "Remember Jon's gallery opening.");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{john}");
        await page.Locator("[data-testid='person-actions']").GetByRole(AriaRole.Button, new() { Name = "More", Exact = true }).ClickAsync();
        await page.Locator("[data-testid='person-merge']").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{john}/merge$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Merge profiles", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='merge-suggestion']")).ToContainTextAsync("Jon Smith");
        await Expect(page.Locator("[data-testid='merge-suggestion-reason']")).ToContainTextAsync("Similar name");

        await page.Locator("[data-testid='merge-suggestion']").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{john}/merge\\?with={jon}$"));
        await Expect(page.Locator("[data-testid='merge-heads']")).ToContainTextAsync("John Smith");
        await Expect(page.Locator("[data-testid='merge-heads']")).ToContainTextAsync("Jon Smith");
        await Expect(page.Locator("[data-testid='merge-field']")).ToHaveCountAsync(2); // Name and Details

        // The name stays John Smith (the default); both details are kept.
        await Expect(Field(page, "Name").Locator("input[data-testid='merge-choice-primary']")).ToBeCheckedAsync();

        // Side by side on a desktop: the two sides of a choice share a row, "Keep both" sits under them.
        var sides = Field(page, "Details").Locator("label.mud-radio");
        var thisSide = await sides.Nth(0).BoundingBoxAsync();
        var otherSide = await sides.Nth(1).BoundingBoxAsync();
        var both = await sides.Nth(2).BoundingBoxAsync();
        otherSide!.X.Should().BeGreaterThan(thisSide!.X + thisSide.Width - 1);
        otherSide.Y.Should().BeApproximately(thisSide.Y, 2);
        both!.Y.Should().BeGreaterThan(thisSide.Y + 20);

        await Field(page, "Details").Locator("input[data-testid='merge-choice-both']").CheckAsync();
        await Expect(page.Locator("[data-testid='merge-preview-name']")).ToHaveTextAsync("John Smith");
        await Expect(page.Locator("[data-testid='merge-preview-details']")).ToContainTextAsync("Met at the chess club.");
        await Expect(page.Locator("[data-testid='merge-preview-details']")).ToContainTextAsync("Climbs on Tuesdays.");
        await Expect(page.Locator("[data-testid='merge-preview-contact-method']")).ToHaveCountAsync(2);
        await Expect(page.Locator("[data-testid='merge-preview-repeated']")).ToHaveTextAsync("1 repeated detail combined");
        await Expect(page.Locator("[data-testid='merge-preview-tag']")).ToHaveTextAsync(["Chess", "Climbing"]);

        await ConfirmMergeAsync(page);

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{john}$"));
        var snackbar = page.Locator(".mud-snackbar", new() { HasText = "Profiles merged" });
        await Expect(snackbar).ToBeVisibleAsync();
        await Expect(snackbar).Not.ToContainTextAsync("John");
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync("John Smith");
        await Expect(page.Locator("[data-testid='person-contact-method']")).ToHaveCountAsync(2);
        await Expect(page.Locator("[data-testid='person-contact-method-heading']")).ToHaveTextAsync(["Email · Work", "Phone · Mobile"]);
        await Expect(page.Locator("[data-testid='person-tag']")).ToHaveTextAsync(["Chess", "Climbing"]);
        await Expect(page.Locator("[data-testid='person-details']")).ToContainTextAsync("Met at the chess club.");
        await Expect(page.Locator("[data-testid='person-details']")).ToContainTextAsync("Climbs on Tuesdays.");
        await Expect(page.Locator("[data-testid='timeline-entry-text']")).ToHaveCountAsync(4);
        (await page.Locator("[data-testid='timeline-entry-text']").AllTextContentsAsync())
            .Should().BeEquivalentTo(
            [
                "We visited the museum together.",
                "Jon sent the gallery opening details.",
                "Remember John's observatory story.",
                "Remember Jon's gallery opening.",
            ]);
        await Expect(page.GetByTestId("pinned-note-text")).ToHaveTextAsync("Remember John's observatory story.");

        // One Smith in the list.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
        await Expect(page.Locator("[data-testid='people-list-item']")).ToHaveCountAsync(1);

        // The removed profile is gone, its tag is not, and the service agrees.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{jon}");
        await Expect(page.Locator("[data-testid='person-not-found']")).ToBeVisibleAsync();
        (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, jon)).Should().BeNull();
        (await PeopleTestHelpers.TagNamesAsync(fixture.App, ownerId)).Should().Equal("Chess", "Climbing");
        var merged = await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, john);
        merged!.ContactMethods.Select(c => c.Value).Should().Equal("john@example.com", "+44 7700 900123");
        merged.ContactMethods.Select(c => c.Label).Should().Equal("Work", "Mobile");
        using var verify = fixture.App.CreateRealScope();
        var interactionDb = verify.ServiceProvider.GetRequiredService<Relio.Data.RelioDbContext>();
        var mergedNotes = await interactionDb.Notes.AsNoTracking()
            .Where(note => note.OwnerId == ownerId)
            .OrderBy(note => note.Text)
            .ToListAsync();
        mergedNotes.Should().HaveCount(2);
        mergedNotes.Should().OnlyContain(note => note.PersonId == john, "both notes move to the kept profile");
        mergedNotes.Single(note => note.IsPinned).Text.Should().Be("Remember John's observatory story.");
        mergedNotes.Should().ContainSingle(note => !note.IsPinned && note.Text == "Remember Jon's gallery opening.");
        var sharedParticipants = await interactionDb.InteractionParticipants.AsNoTracking()
            .Where(participant => participant.InteractionId == sharedInteractionId)
            .Select(participant => participant.PersonId)
            .ToListAsync();
        sharedParticipants.Should().ContainSingle().Which.Should().Be(john, "the shared row is not repeated after merging");
        var movedParticipants = await interactionDb.InteractionParticipants.AsNoTracking()
            .Where(participant => participant.InteractionId == duplicateOnlyInteractionId)
            .Select(participant => participant.PersonId)
            .ToListAsync();
        movedParticipants.Should().ContainSingle().Which.Should().Be(john, "the duplicate-only interaction moves to the kept profile");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Going_back_after_a_merge_never_lands_on_the_address_of_the_removed_profile()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var (john, jon) = await SeedJohnAndJonAsync(ownerId);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{john}");
        await page.Locator("[data-testid='person-actions']").GetByRole(AriaRole.Button, new() { Name = "More", Exact = true }).ClickAsync();
        await page.Locator("[data-testid='person-merge']").ClickAsync();
        await page.Locator("[data-testid='merge-suggestion']").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex($"\\?with={jon}$"));
        await ConfirmMergeAsync(page);
        await Expect(page).ToHaveURLAsync(new Regex($"/people/{john}$"));

        await page.GoBackAsync();

        // The step with ?with={removed} was replaced by the profile, so Back reaches at most the first step.
        page.Url.Should().NotContain("with=");
        await Expect(page.Locator("[data-testid='person-not-found']")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Cancelling_the_confirmation_keeps_both_profiles()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var (john, jon) = await SeedJohnAndJonAsync(ownerId);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{john}/merge?with={jon}");
        await page.Locator("[data-testid='merge-submit']").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToContainTextAsync("Merge Jon Smith into John Smith?");
        await Expect(page.Locator(".mud-dialog")).ToContainTextAsync("This can't be undone.");
        await page.Locator("[data-testid='confirm-dialog-cancel']").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);

        await page.Locator("[data-testid='merge-submit']").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{john}/merge\\?with={jon}$"));
        (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId)).Should().HaveCount(2);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Merge_instead_from_the_duplicate_warning_opens_the_merge_page()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var john = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = "John", LastName = "Smith" });
        var ada = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = "Ada", LastName = "Byron" });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{ada}/edit");
        await page.GetByLabel("First name").FillAsync("Jon");
        await page.GetByLabel("Last name").FillAsync("Smith");
        await page.Locator("[data-testid='person-form-save']").ClickAsync();

        await Expect(page.Locator("[data-testid='person-duplicate-warning']")).ToBeVisibleAsync();
        await page.Locator("[data-testid='person-duplicate-merge']").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{ada}/merge\\?with={john}$"));
        await Expect(page.Locator("[data-testid='merge-heads']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='merge-heads']")).ToContainTextAsync("Ada Byron");
        await Expect(page.Locator("[data-testid='merge-heads']")).ToContainTextAsync("John Smith");
        (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, ada))!.FirstName.Should().Be("Ada", "merging instead did not save the rename");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task An_archived_duplicate_can_be_merged_and_the_result_stays_active()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var (john, jon) = await SeedJohnAndJonAsync(ownerId);
        await PeopleTestHelpers.ArchivePersonAsync(fixture.App, ownerId, jon);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{john}/merge");
        await Expect(page.Locator("[data-testid='merge-suggestion-reason']")).ToContainTextAsync("Archived");
        await page.Locator("[data-testid='merge-suggestion']").ClickAsync();

        await Expect(Field(page, "ArchivedState")).ToBeVisibleAsync();
        await Expect(Field(page, "ArchivedState").Locator("input[data-testid='merge-choice-primary']")).ToBeCheckedAsync();
        await Expect(page.Locator("[data-testid='merge-preview-status']")).ToHaveTextAsync("Active");

        await ConfirmMergeAsync(page);

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{john}$"));
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync("John Smith");
        await Expect(page.Locator("[data-testid='person-archived']")).ToHaveCountAsync(0);
        (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, john))!.IsArchived.Should().BeFalse();
        (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, jon)).Should().BeNull();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task The_picker_finds_any_other_profile_by_name()
    {
        var page = await fixture.NewPageAsync();
        var ownerId = await RegisterFreshUserAsync(page);
        var ada = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = "Ada", LastName = "Lovelace" });
        var jose = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = "José", LastName = "García" });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{ada}/merge");
        await Expect(page.Locator("[data-testid='merge-suggestions']")).ToHaveCountAsync(0);
        await page.Locator("[data-testid='merge-picker-field'] input").FillAsync("jose");
        await page.GetByRole(AriaRole.Option, new() { Name = "José García" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{ada}/merge\\?with={jose}$"));
        await Expect(page.Locator("[data-testid='merge-heads']")).ToContainTextAsync("José García");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Another_users_profile_can_never_be_merged()
    {
        var pageA = await fixture.NewPageAsync();
        var ownerA = await RegisterFreshUserAsync(pageA);
        await RelioAppFixture.ClosePageAsync(pageA);
        var secret = $"Zelda{Guid.NewGuid():N}"[..18];
        var aliceId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerA, new CreatePersonRequest
        {
            FirstName = secret,
            LastName = "Smith",
            NewTagNames = ["Chess"],
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "alice@example.com")],
        });

        var pageB = await fixture.NewPageAsync();
        var ownerB = await RegisterFreshUserAsync(pageB);
        var bobId = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerB, new CreatePersonRequest { FirstName = "Bob", LastName = "Smith" });

        // A's profile is "not in your list" for B, exactly like a profile that does not exist.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, $"/people/{aliceId}/merge");
        await Expect(pageB.Locator("[data-testid='person-not-found']")).ToBeVisibleAsync();
        (await pageB.ContentAsync()).Should().NotContain(secret);

        // As the duplicate: the page answers "that profile isn't in your list" and shows nothing of A's.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageB, $"/people/{bobId}/merge?with={aliceId}");
        await Expect(pageB.Locator("[data-testid='merge-other-missing']")).ToBeVisibleAsync();
        await Expect(pageB.Locator("[data-testid='merge-submit']")).ToHaveCountAsync(0);
        (await pageB.ContentAsync()).Should().NotContain(secret);

        // And through the service as B, in either position.
        (await PeopleTestHelpers.MergeAsync(fixture.App, ownerB, new MergePeopleRequest { PrimaryId = bobId, DuplicateId = aliceId }))
            .Should().Be(MergeOutcome.NotFound);
        (await PeopleTestHelpers.MergeAsync(fixture.App, ownerB, new MergePeopleRequest { PrimaryId = aliceId, DuplicateId = bobId }))
            .Should().Be(MergeOutcome.NotFound);
        (await PeopleTestHelpers.ListMergeCandidatesAsync(fixture.App, ownerB, aliceId)).Should().BeNull();
        (await PeopleTestHelpers.ListMergeCandidatesAsync(fixture.App, ownerB, bobId))!.Others.Should().BeEmpty();

        var alice = await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerA, aliceId);
        alice!.FirstName.Should().Be(secret);
        alice.ContactMethods.Should().ContainSingle();
        alice.Tags.Should().ContainSingle();
        (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerB, bobId))!.ContactMethods.Should().BeEmpty();

        await RelioAppFixture.ClosePageAsync(pageB);
    }

    [Fact]
    public async Task Merging_works_on_a_phone()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        var ownerId = await RegisterFreshUserAsync(page);
        var (john, jon) = await SeedJohnAndJonAsync(ownerId);
        var noSidewaysScroll = "document.documentElement.scrollWidth <= window.innerWidth";

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{john}/merge");
        await Expect(page.Locator("[data-testid='merge-suggestion']")).ToBeVisibleAsync();
        (await page.EvaluateAsync<bool>(noSidewaysScroll)).Should().BeTrue("step 1 must not scroll sideways");
        (await page.Locator("[data-testid='merge-suggestion']").BoundingBoxAsync())!.Height.Should().BeGreaterThanOrEqualTo(44);
        await page.Locator("[data-testid='merge-suggestion']").ClickAsync();

        await Expect(page.Locator("[data-testid='merge-heads']")).ToBeVisibleAsync();
        (await page.EvaluateAsync<bool>(noSidewaysScroll)).Should().BeTrue("step 2 must not scroll sideways");

        // The two sides of a choice are stacked, and each row is a comfortable target.
        var name = Field(page, "Name");
        var rows = name.Locator("label.mud-radio");
        await Expect(rows).ToHaveCountAsync(2);
        var first = await rows.Nth(0).BoundingBoxAsync();
        var second = await rows.Nth(1).BoundingBoxAsync();
        second!.Y.Should().BeGreaterThan(first!.Y + first.Height - 1, "the radios are stacked on a phone");
        first.Height.Should().BeGreaterThanOrEqualTo(44);
        second.Height.Should().BeGreaterThanOrEqualTo(44);
        var heads = await page.Locator("[data-testid='merge-heads'] > div").EvaluateAllAsync<double[]>("els => els.map(e => e.getBoundingClientRect().top)");
        heads[1].Should().BeGreaterThan(heads[0], "the two profiles are stacked");

        var submit = page.Locator("[data-testid='merge-submit']");
        await submit.ScrollIntoViewIfNeededAsync();
        var submitBox = await submit.BoundingBoxAsync();
        submitBox!.Width.Should().BeGreaterThan((float)(Viewports.Phone.Width * 0.7), "the button is full width on a phone");
        submitBox.Height.Should().BeGreaterThanOrEqualTo(44);

        await submit.ClickAsync();
        var dialog = page.Locator(".mud-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        var dialogBox = await dialog.BoundingBoxAsync();
        (dialogBox!.X + dialogBox.Width).Should().BeLessThanOrEqualTo(Viewports.Phone.Width);
        dialogBox.X.Should().BeGreaterThanOrEqualTo(0);
        await page.Locator("[data-testid='confirm-dialog-confirm']").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"/people/{john}$"));
        await Expect(page.Locator("[data-testid='person-name']")).ToHaveTextAsync("John Smith");
        (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, jon)).Should().BeNull();

        await RelioAppFixture.ClosePageAsync(page);
    }
}
