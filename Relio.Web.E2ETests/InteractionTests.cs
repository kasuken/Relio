using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>Exercises shared interaction entry, editing and deletion in a real profile circuit.</summary>
[Collection(RelioAppCollection.Name)]
public sealed class InteractionTests(RelioAppFixture fixture)
{
    private static async Task ChooseAsync(IPage page, string testId, string option)
    {
        var label = testId switch
        {
            "interaction-kind" => "Type",
            "timeline-filter" => "Show",
            _ => throw new ArgumentOutOfRangeException(nameof(testId)),
        };
        await page.GetByRole(AriaRole.Combobox, new() { Name = label, Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
    }

    private static async Task ChooseParticipantAsync(IPage page, string displayName)
    {
        var field = page.GetByRole(AriaRole.Combobox, new() { Name = "People", Exact = true });
        var option = page.GetByRole(AriaRole.Option, new() { Name = displayName, Exact = true });
        if (!await option.IsVisibleAsync())
        {
            await field.ClickAsync();
        }

        await option.ClickAsync();
    }

    [Fact]
    public async Task One_interaction_is_shared_edited_and_deleted_from_every_participant_profile()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("interaction");
        await RegisterAsync(page, email, StrongPassword);
        var ownerId = (await GetUserAsync(fixture.App, email))!.Id;

        var ada = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
        });
        var bea = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = "Bea",
            LastName = "Byron",
        });
        var cleo = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = "Cleo",
            LastName = "Chen",
        });
        var dee = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest
        {
            FirstName = "Dee",
            LastName = "Lovelace",
        });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{ada}");
        await page.Locator("[data-testid='person-log-interaction']").ClickAsync();
        await Expect(page.Locator("[data-testid='interaction-editor']")).ToBeVisibleAsync();
        await ChooseAsync(page, "interaction-kind", "Meeting");
        await ChooseParticipantAsync(page, "Bea Byron");
        await ChooseParticipantAsync(page, "Cleo Chen");
        await page.GetByLabel("What happened?", new() { Exact = true })
            .FillAsync("We talked about the observatory.");
        await page.Locator("[data-testid='interaction-save']").ClickAsync();
        await Expect(page.Locator("[data-testid='interaction-editor']"))
            .ToHaveCountAsync(0, new() { Timeout = 15_000 });

        var entry = page.Locator("[data-testid='timeline-entry']");
        await Expect(entry).ToContainTextAsync("We talked about the observatory.");
        await Expect(entry.Locator("[data-testid='timeline-entry-participants']"))
            .ToContainTextAsync("Bea Byron");
        await Expect(entry.Locator("[data-testid='timeline-entry-participants']"))
            .ToContainTextAsync("Cleo Chen");
        await Expect(page.Locator("[data-testid='person-last-contacted']")).ToHaveTextAsync("Today");

        await entry.Locator("[data-testid='timeline-edit-interaction']").ClickAsync();
        await Expect(page.Locator("[data-testid='interaction-editor']")).ToContainTextAsync("Changes affect everyone.");
        await page.GetByLabel("What happened?", new() { Exact = true })
            .FillAsync("We planned a visit to the observatory.");
        await ChooseParticipantAsync(page, "Bea Byron");
        await ChooseParticipantAsync(page, "Dee Lovelace");
        await page.Locator("[data-testid='interaction-save']").ClickAsync();
        await Expect(page.Locator("[data-testid='interaction-editor']"))
            .ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Expect(page.Locator("[data-testid='timeline-entry-text']"))
            .ToHaveTextAsync("We planned a visit to the observatory.");
        await Expect(page.Locator("[data-testid='timeline-entry-participants']"))
            .ToContainTextAsync("Cleo Chen");
        await Expect(page.Locator("[data-testid='timeline-entry-participants']"))
            .ToContainTextAsync("Dee Lovelace");
        await Expect(page.Locator("[data-testid='timeline-entry-participants']"))
            .Not.ToContainTextAsync("Bea Byron");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{bea}");
        await Expect(page.Locator("[data-testid='timeline-empty']")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("person-last-contacted")).ToHaveTextAsync("Not contacted yet");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{dee}");
        var deeEntry = page.Locator("[data-testid='timeline-entry']");
        await Expect(deeEntry).ToContainTextAsync("We planned a visit to the observatory.");
        await Expect(deeEntry.Locator("[data-testid='timeline-entry-participants']"))
            .ToContainTextAsync("Ada Lovelace");
        await Expect(deeEntry.Locator("[data-testid='timeline-entry-participants']"))
            .ToContainTextAsync("Cleo Chen");
        await Expect(page.GetByTestId("person-last-contacted")).ToHaveTextAsync("Today");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{cleo}");
        await Expect(page.Locator("[data-testid='timeline-entry-text']"))
            .ToHaveTextAsync("We planned a visit to the observatory.");
        await page.Locator("[data-testid='timeline-delete-interaction']").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToContainTextAsync("removed from every profile listed");
        await page.Locator("[data-testid='confirm-dialog-confirm']").ClickAsync();
        await Expect(page.Locator(".mud-snackbar", new() { HasText = "Interaction deleted" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='timeline-empty']")).ToBeVisibleAsync();

        foreach (var personId in new[] { ada, bea, cleo, dee })
        {
            (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, personId))!
                .LastContactedOn.Should().BeNull("deleting a shared interaction clears its last-contact date everywhere");
        }

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_future_interaction_date_is_rejected_without_saving()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("interaction-date");
        await RegisterAsync(page, email, StrongPassword);
        var ownerId = (await GetUserAsync(fixture.App, email))!.Id;
        var person = await PeopleTestHelpers.CreatePersonAsync(fixture.App, ownerId, new CreatePersonRequest { FirstName = "Ada" });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{person}");
        await page.Locator("[data-testid='person-log-interaction']").ClickAsync();
        await page.GetByLabel("Date", new() { Exact = true }).FillAsync("1 Jan 9999");
        await page.GetByLabel("What happened?", new() { Exact = true }).FillAsync("A future visit.");
        await page.Locator("[data-testid='interaction-save']").ClickAsync();

        await Expect(page.Locator("[data-testid='interaction-date-error']"))
            .ToHaveTextAsync("The date can't be in the future.");
        await Expect(page.Locator("[data-testid='timeline-empty']")).ToBeVisibleAsync();
        (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, person))!.LastContactedOn.Should().BeNull();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Timeline_filters_and_loads_only_the_next_page_of_a_long_history()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("timeline");
        await RegisterAsync(page, email, StrongPassword);
        var ownerId = (await GetUserAsync(fixture.App, email))!.Id;
        var person = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App,
            ownerId,
            new CreatePersonRequest { FirstName = "Ada" });
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        await PeopleTestHelpers.SeedInteractionsAsync(fixture.App, ownerId, person, today, count: 1_000);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{person}");
        await Expect(page.Locator("[data-testid='timeline-entry']")).ToHaveCountAsync(50);
        await Expect(page.Locator("[data-testid='timeline-load-more']")).ToBeVisibleAsync();

        await ChooseAsync(page, "timeline-filter", "Difficult moments");
        await Expect(page.Locator("[data-testid='timeline-empty']"))
            .ToContainTextAsync("Difficult moments will appear here when that feature is available.");

        await ChooseAsync(page, "timeline-filter", "Interactions");
        await Expect(page.Locator("[data-testid='timeline-entry']")).ToHaveCountAsync(50);
        await page.Locator("[data-testid='timeline-load-more']").ClickAsync();
        await Expect(page.Locator("[data-testid='timeline-entry']")).ToHaveCountAsync(100);

        await RelioAppFixture.ClosePageAsync(page);
    }
}
