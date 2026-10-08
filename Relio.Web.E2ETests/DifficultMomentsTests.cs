using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>Exercises difficult moments recording, timeline display, editing, deletion and overview reflection page.</summary>
[Collection(RelioAppCollection.Name)]
public sealed class DifficultMomentsTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Difficult_moments_can_be_recorded_edited_reflected_on_and_deleted()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("moments");
        await RegisterAsync(page, email, StrongPassword);
        var ownerId = (await GetUserAsync(fixture.App, email))!.Id;
        var personId = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App,
            ownerId,
            new Relio.Application.People.CreatePersonRequest { FirstName = "Ada", LastName = "Lovelace" });

        // 1. Navigate to profile and open recording form
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}");
        await page.GetByTestId("person-record-moment").ClickAsync();

        // Fill moment form
        await page.Locator("[data-testid='moment-description-field'] textarea")
            .FillAsync("A difficult misunderstanding regarding project deadlines.");
        await page.Locator("[data-testid='moment-trigger-field'] textarea")
            .FillAsync("Misaligned expectations on scope.");
        await page.Locator("[data-testid='moment-resolution-field'] textarea")
            .FillAsync("Scheduled a alignment session and agreed on priorities.");
        await page.Locator("[data-testid='moment-lessons-field'] textarea")
            .FillAsync("Clarify milestone criteria before kicking off.");
        await page.GetByTestId("moment-save").ClickAsync();

        // 2. Verify on timeline
        var timelineEntry = page.GetByTestId("timeline-entry-text");
        await Expect(timelineEntry).ToHaveTextAsync("A difficult misunderstanding regarding project deadlines.");
        await Expect(page.GetByTestId("timeline-moment-status")).ToHaveTextAsync("Open");
        await Expect(page.GetByTestId("timeline-moment-trigger"))
            .ToContainTextAsync("Misaligned expectations on scope.");

        // 3. Edit the moment from the timeline
        await page.GetByTestId("timeline-edit-moment").ClickAsync();
        var descInput = page.Locator("[data-testid='moment-description-field'] textarea");
        await Expect(descInput).ToHaveValueAsync("A difficult misunderstanding regarding project deadlines.");
        await descInput.FillAsync("A resolved misunderstanding regarding project deadlines.");
        await page.GetByTestId("moment-save").ClickAsync();

        await Expect(page.GetByTestId("timeline-entry-text"))
            .ToHaveTextAsync("A resolved misunderstanding regarding project deadlines.");

        // 4. Navigate to /difficult-moments overview
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/difficult-moments");
        await Expect(page.Locator("[data-testid^='moment-card-']")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid^='moment-card-']")).ToContainTextAsync("Ada Lovelace");
        await Expect(page.Locator("[data-testid^='moment-card-']"))
            .ToContainTextAsync("A resolved misunderstanding regarding project deadlines.");

        // 5. Navigate back to profile and delete moment
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}");
        await page.GetByTestId("timeline-delete-moment").ClickAsync();
        await page.Locator("[data-testid='confirm-dialog-confirm']").ClickAsync();

        // Verify removed from timeline
        await Expect(page.GetByTestId("timeline-entry-text")).ToHaveCountAsync(0);

        // Verify removed from overview
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/difficult-moments");
        await Expect(page.GetByTestId("moments-empty")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No difficult moments recorded", Exact = true }))
            .ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }
}
