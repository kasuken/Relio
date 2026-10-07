using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.Security;
using Relio.Domain;
using Relio.Data;
using Relio.Data.Time;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>Exercises global interaction entry points and quick logging in a real phone-sized circuit.</summary>
[Collection(RelioAppCollection.Name)]
public sealed class QuickLogInteractionTests(RelioAppFixture fixture)
{
    private static async Task TabToAsync(IPage page, ILocator target, int maxTabs = 20)
    {
        for (var index = 0; index < maxTabs; index++)
        {
            if (await target.EvaluateAsync<bool>(
                "element => element === document.activeElement || element.contains(document.activeElement)"))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        (await target.EvaluateAsync<bool>(
            "element => element === document.activeElement || element.contains(document.activeElement)"))
            .Should()
            .BeTrue("keyboard navigation should reach the requested control");
    }

    [Fact]
    public async Task Global_quick_log_is_available_from_the_dashboard_people_list_and_appbar()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);

        try
        {
            var email = NewEmail("quick-log");
            await RegisterAsync(page, email, StrongPassword);
            await page.SetViewportSizeAsync(360, 800);
            var ownerId = (await GetUserAsync(fixture.App, email))!.Id;
            var today = await SetTimeZoneAndGetTodayAsync(fixture.App, ownerId, "Pacific/Kiritimati");

            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");
            var appbarLog = page.GetByTestId("appbar-log-interaction");
            await Expect(appbarLog).ToBeVisibleAsync();
            await Expect(appbarLog).ToHaveAttributeAsync("aria-label", "Log an interaction");
            await Expect(appbarLog).ToHaveAttributeAsync("href", "/interactions/new");
            await Expect(page.GetByTestId("dashboard-log-interaction"))
                .ToHaveAttributeAsync("href", "/interactions/new");

            var appbarFits = await appbarLog.EvaluateAsync<bool>(
                "element => { const bounds = element.getBoundingClientRect(); const appbar = element.closest('.mud-appbar'); return appbar !== null && bounds.left >= 0 && bounds.right <= window.innerWidth && appbar.scrollWidth <= appbar.clientWidth; }");
            appbarFits.Should().BeTrue("the quick-log shortcut and appbar must fit a 360px viewport");

            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/interactions/new");
            await Expect(page).ToHaveTitleAsync("Log an interaction - Relio");
            await Expect(page.GetByTestId("quick-log-no-people")).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add a person", Exact = true }))
                .ToBeVisibleAsync();

            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
            await Expect(page.GetByTestId("people-empty-log-interaction"))
                .ToHaveAttributeAsync("href", "/interactions/new");

            await PeopleTestHelpers.SeedPeopleAsync(
                fixture.App,
                ownerId,
                new SeedPerson("José", "García"),
                new SeedPerson("Avery", "Archived", IsArchived: true));
            var activePerson = (await PeopleTestHelpers.ListPeopleAsync(fixture.App, ownerId))
                .Single(person => person.FirstName == "José" && !person.IsArchived);

            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
            var peopleLog = page.GetByTestId("people-log-interaction");
            await Expect(peopleLog).ToHaveAttributeAsync("href", "/interactions/new");
            await TabToAsync(page, peopleLog);
            await page.Keyboard.PressAsync("Enter");
            await Expect(page).ToHaveURLAsync(new Regex("/interactions/new$"));

            var personInput = page.GetByTestId("quick-log-person-picker");
            await TabToAsync(page, personInput);
            await Expect(page.GetByRole(AriaRole.Option)).ToHaveCountAsync(1);
            await page.Keyboard.TypeAsync("archived");
            await Expect(page.GetByRole(AriaRole.Option)).ToHaveCountAsync(0);

            await personInput.PressAsync("Control+A");
            await personInput.PressAsync("Backspace");
            await page.Keyboard.TypeAsync("jose");
            var personOption = page.GetByRole(
                AriaRole.Option,
                new() { Name = "José García", Exact = true });
            await Expect(personOption).ToBeVisibleAsync();
            await personInput.PressAsync("ArrowDown");
            await personInput.PressAsync("Enter");
            await Expect(page.GetByTestId("interaction-editor")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("interaction-participants-field")).ToHaveCountAsync(0);

            var dateInput = page.GetByTestId("interaction-date-field").Locator("input");
            var expectedDate = today.ToDateTime(TimeOnly.MinValue)
                .ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            await Expect(dateInput).ToHaveValueAsync(expectedDate);

            var typeInput = page.GetByRole(AriaRole.Combobox, new() { Name = "Type", Exact = true });
            await TabToAsync(page, typeInput);
            await page.Keyboard.PressAsync("Enter");
            await Expect(typeInput).ToHaveAttributeAsync("aria-expanded", "true");

            var typeListbox = page.GetByRole(AriaRole.Listbox);
            await Expect(typeListbox).ToBeVisibleAsync();
            var callOption = typeListbox.GetByRole(
                AriaRole.Option,
                new() { Name = "Call", Exact = true });
            var meetingOption = typeListbox.GetByRole(
                AriaRole.Option,
                new() { Name = "Meeting", Exact = true });
            await Expect(callOption).ToBeVisibleAsync();
            await Expect(meetingOption).ToBeVisibleAsync();
            var callOptionId = await callOption.GetAttributeAsync("id")
                ?? throw new InvalidOperationException("The current interaction type option must have an id.");
            var meetingOptionId = await meetingOption.GetAttributeAsync("id")
                ?? throw new InvalidOperationException("The meeting interaction type option must have an id.");

            await Expect(typeInput).ToHaveAttributeAsync("aria-activedescendant", callOptionId);
            await page.Keyboard.PressAsync("ArrowDown");
            await Expect(typeInput).ToHaveAttributeAsync("aria-activedescendant", meetingOptionId);
            await page.Keyboard.PressAsync("Enter");
            await Expect(typeInput).ToContainTextAsync("Meeting");

            var description = page.GetByLabel("What happened?", new() { Exact = true });
            await TabToAsync(page, description);
            await page.Keyboard.TypeAsync("We talked about the garden.");
            await Expect(description).ToHaveValueAsync("We talked about the garden.");
            await page.Keyboard.PressAsync("Tab");
            (await page.GetByTestId("interaction-save")
                .EvaluateAsync<bool>("element => element === document.activeElement")).Should().BeTrue();
            await page.Keyboard.PressAsync("Enter");

            await Expect(page).ToHaveURLAsync(new Regex($"/people/{activePerson.Id}$"));
            await Expect(page.Locator(".mud-snackbar", new() { HasText = "Interaction logged" }))
                .ToBeVisibleAsync();
            var entry = page.GetByTestId("timeline-entry");
            await Expect(entry).ToContainTextAsync("We talked about the garden.");
            await Expect(entry.GetByTestId("timeline-entry-kind")).ToContainTextAsync("Meeting");
            await Expect(entry.GetByTestId("timeline-entry-date"))
                .ToHaveAttributeAsync("datetime", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            await Expect(page.GetByTestId("person-last-contacted")).ToHaveTextAsync("Today");
            (await PeopleTestHelpers.GetPersonAsync(fixture.App, ownerId, activePerson.Id))!
                .LastContactedOn.Should().Be(today);
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    private static async Task<DateOnly> SetTimeZoneAndGetTodayAsync(
        RelioWebAppFactory app,
        string ownerId,
        string ianaTimeZoneId)
    {
        using var scope = app.CreateRealScope();
        var service = new UserTimeZoneService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        await service.SetTimeZoneAsync(ianaTimeZoneId);
        return await service.GetTodayAsync();
    }

    private sealed class OwnerCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }
}
