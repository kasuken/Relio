using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.Interactions;
using Relio.Application.People;
using Relio.Application.Time;
using Relio.Data;
using Relio.Domain;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class DashboardOverviewTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Dashboard_shows_fresh_users_upcoming_items_reach_outs_interactions_and_recent_people()
    {
        var page = await fixture.NewPageAsync();
        var email = AccountTestHelpers.NewEmail("dashboard-overview");
        await AccountTestHelpers.RegisterAsync(page, email, AccountTestHelpers.StrongPassword);

        var user = await AccountTestHelpers.GetUserAsync(fixture.App, email);
        user.Should().NotBeNull();
        var ownerId = user!.Id;
        DateOnly today;
        using (var scope = fixture.App.CreateRealScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
            var profile = await dbContext.UserProfiles.AsNoTracking()
                .SingleAsync(item => item.OwnerId == ownerId);
            var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            today = UserCalendar.Today(timeProvider, TimeZoneIds.Parse(profile.TimeZoneId));
        }

        var birthdayDate = today.AddDays(3);
        var reminderPersonId = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App,
            ownerId,
            new CreatePersonRequest
            {
                FirstName = "Iris",
                LastName = "Wells",
                BirthdayMonth = birthdayDate.Month,
                BirthdayDay = birthdayDate.Day,
            });
        var reachOutPersonId = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App,
            ownerId,
            new CreatePersonRequest
            {
                FirstName = "Nico",
                LastName = "North",
                StayInTouchCadenceDays = 1,
            });
        var interactionPersonId = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App,
            ownerId,
            new CreatePersonRequest
            {
                FirstName = "Mina",
                LastName = "Vale",
            });

        var reminderId = await PeopleTestHelpers.CreateReminderAsync(
            fixture.App,
            ownerId,
            reminderPersonId,
            "Ask about the new studio",
            today.AddDays(1));
        var interactionId = await PeopleTestHelpers.CreateInteractionAsync(
            fixture.App,
            ownerId,
            new CreateInteractionRequest
            {
                ProfilePersonId = interactionPersonId,
                OccurredOn = today.AddDays(-1),
                Kind = InteractionKind.Call,
                Description = "Talked about the garden.",
                ParticipantIds = [interactionPersonId],
            });

        using (var scope = fixture.App.CreateRealScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
            var reachOutPerson = await dbContext.People.SingleAsync(
                person => person.Id == reachOutPersonId && person.OwnerId == ownerId);
            reachOutPerson.LastContactedOn = today.AddDays(-10);
            await dbContext.SaveChangesAsync();
        }

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator($"[data-testid='dashboard-reminder-{reminderId}']")).ToBeVisibleAsync();
        await Expect(page.GetByText("Ask about the new studio")).ToBeVisibleAsync();
        await Expect(page.Locator($"[data-testid='dashboard-birthday-{reminderPersonId}']")).ToBeVisibleAsync();
        await Expect(page.GetByText("Birthday in 3 days")).ToBeVisibleAsync();
        await Expect(page.Locator($"[data-testid='dashboard-reach-out-{reachOutPersonId}']")).ToBeVisibleAsync();
        await Expect(page.GetByText("9 days overdue")).ToBeVisibleAsync();
        await Expect(page.Locator($"[data-testid='dashboard-interaction-{interactionId}']")).ToBeVisibleAsync();
        await Expect(page.GetByText("Talked about the garden.")).ToBeVisibleAsync();
        await Expect(page.Locator($"[data-testid='dashboard-recent-person-{reminderPersonId}']")).ToBeVisibleAsync();

        await Expect(page.Locator($"[data-testid='dashboard-reminder-{reminderId}'] a"))
            .ToHaveAttributeAsync("href", $"/people/{reminderPersonId}");
        await Expect(page.Locator($"[data-testid='dashboard-interaction-{interactionId}'] a"))
            .ToHaveAttributeAsync("href", $"/people/{interactionPersonId}");
        await Expect(page.Locator($"[data-testid='dashboard-recent-person-{reminderPersonId}'] a"))
            .ToHaveAttributeAsync("href", $"/people/{reminderPersonId}");

        await RelioAppFixture.ClosePageAsync(page);
    }
}
