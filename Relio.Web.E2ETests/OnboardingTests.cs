using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Issue #48: a new user can complete or skip the short onboarding guide, and the choice remains
/// private to that account across reloads and sign-ins.
/// </summary>
[Collection(RelioAppCollection.Name)]
public sealed class OnboardingTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task New_user_can_log_a_first_interaction_in_under_two_minutes_and_stays_dismissed()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("onboarding");
        const string description = "Talked about the garden.";
        var timer = Stopwatch.StartNew();

        try
        {
            await RegisterAsync(page, email, StrongPassword);
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            await Expect(page).ToHaveURLAsync(new Regex("/onboarding$"));
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Get started", Exact = true }))
                .ToBeVisibleAsync();
            await Expect(page.Locator("[data-testid='onboarding-page']"))
                .ToContainTextAsync("third-party analytics or tracking");

            await page.Locator("[data-testid='onboarding-timezone-save']").ClickAsync();
            await Expect(page.Locator("[data-testid='onboarding-person-step']")).ToBeVisibleAsync();
            await page.GetByLabel("First name").FillAsync("Ava");
            await page.Locator("[data-testid='person-form-save']").ClickAsync();
            await Expect(page.Locator("[data-testid='onboarding-interaction-step']")).ToBeVisibleAsync();
            await page.GetByLabel("What happened?").FillAsync(description);
            await page.Locator("[data-testid='interaction-save']").ClickAsync();
            await Expect(page).ToHaveURLAsync(new Regex("/$"));
            await Expect(page.Locator("[data-testid='dashboard-add-person']")).ToBeVisibleAsync();

            timer.Stop();
            timer.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(2));

            Relio.Data.Identity.RelioUser? user;
            using (var userScope = fixture.App.CreateRealScope())
            {
                user = await userScope.ServiceProvider
                    .GetRequiredService<UserManager<RelioUser>>()
                    .FindByEmailAsync(email);
            }

            user.Should().NotBeNull();
            var userId = user!.Id;
            Guid personId;
            using (var dataScope = fixture.App.CreateRealScope())
            {
                var dbContext = dataScope.ServiceProvider.GetRequiredService<RelioDbContext>();
                var profile = await dbContext.UserProfiles.AsNoTracking()
                    .SingleAsync(item => item.OwnerId == userId);
                profile.OnboardingDismissed.Should().BeTrue();

                var person = await dbContext.People.AsNoTracking()
                    .SingleAsync(item => item.OwnerId == userId);
                personId = person.Id;
                person.LastContactedOn.Should().NotBeNull();

                var interaction = await dbContext.Interactions.AsNoTracking()
                    .SingleAsync(item => item.OwnerId == userId);
                interaction.Description.Should().Be(description);
                (await dbContext.InteractionParticipants.AsNoTracking()
                    .Where(item => item.OwnerId == userId && item.InteractionId == interaction.Id)
                    .Select(item => item.PersonId)
                    .ToListAsync()).Should().ContainSingle().Which.Should().Be(personId);
            }

            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}");
            await Expect(page.Locator("[data-testid='person-timeline'] [data-testid='timeline-entry-text']"))
                .ToContainTextAsync(description);
            await page.GotoAsync("/");
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            await Expect(page.Locator("[data-testid='onboarding-prompt']")).ToHaveCountAsync(0);

            var secondPage = await fixture.NewPageAsync();
            try
            {
                await LoginAndWaitForAppAsync(secondPage, email, StrongPassword);
                await Expect(secondPage.Locator("[data-testid='onboarding-prompt']")).ToHaveCountAsync(0);
                await secondPage.GotoAsync("/onboarding");
                await Expect(secondPage).ToHaveURLAsync(new Regex("/$"));
                await Expect(secondPage.Locator("[data-testid='onboarding-timezone-step']")).ToHaveCountAsync(0);
            }
            finally
            {
                await RelioAppFixture.ClosePageAsync(secondPage);
            }
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Skipping_lands_on_the_dashboard_with_the_next_step_and_never_prompts_again()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("onboarding-skip");

        try
        {
            await RegisterAsync(page, email, StrongPassword);
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            await Expect(page).ToHaveURLAsync(new Regex("/onboarding$"));
            await page.Locator("[data-testid='onboarding-skip']").ClickAsync();
            await Expect(page).ToHaveURLAsync(new Regex("/$"));
            await Expect(page.Locator("[data-testid='dashboard-empty']")).ToContainTextAsync("Add person");
            await Expect(page.Locator("[data-testid='onboarding-prompt']")).ToHaveCountAsync(0);

            await page.ReloadAsync();
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            await Expect(page.Locator("[data-testid='onboarding-prompt']")).ToHaveCountAsync(0);

            var secondPage = await fixture.NewPageAsync();
            try
            {
                await LoginAndWaitForAppAsync(secondPage, email, StrongPassword);
                await Expect(secondPage.Locator("[data-testid='dashboard-empty']")).ToContainTextAsync("Add person");
                await Expect(secondPage.Locator("[data-testid='onboarding-prompt']")).ToHaveCountAsync(0);
            }
            finally
            {
                await RelioAppFixture.ClosePageAsync(secondPage);
            }
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }
}
