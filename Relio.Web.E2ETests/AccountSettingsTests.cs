using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #18's settings hub end to end (the display name, time zone and the links to the
/// account pages; <see cref="ChangePasswordTests"/> and <see cref="ChangeEmailTests"/> cover the
/// pages themselves). Every test registers its own fresh user: the shared demo user must never be
/// modified (see <see cref="AccountTestHelpers"/>'s remarks).
/// </summary>
[Collection(RelioAppCollection.Name)]
public class AccountSettingsTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Settings_shows_every_account_section()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("sections");
        await RegisterAsync(page, email, StrongPassword);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");

        foreach (var heading in new[] { "Profile", "Time zone", "Relationship types and tags", "Sign-in and security", "Reminder emails", "Appearance" })
        {
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })).ToBeVisibleAsync();
        }

        await Expect(page.Locator("[data-testid='settings-current-email']")).ToHaveTextAsync(email);
        await Expect(page.Locator("[data-testid='settings-reminder-email-preferences']")).ToBeDisabledAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Display_name_can_be_changed_and_persists_across_reload()
    {
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, NewEmail("display"), StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");

        var field = page.GetByLabel("Display name");
        await field.FillAsync("  Marta Rossi  ");
        await page.Locator("[data-testid='settings-display-name-save']").ClickAsync();
        await Expect(page.GetByText("Display name saved")).ToBeVisibleAsync();

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");

        await Expect(page.GetByLabel("Display name")).ToHaveValueAsync("Marta Rossi");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Browser_time_zone_is_suggested_and_can_be_saved()
    {
        var email = NewEmail("zone");

        // Sign-up reads the browser's time zone (Register.razor), so this account starts in Pago Pago...
        var signUpPage = await fixture.NewPageAsync(timezoneId: "Pacific/Pago_Pago");
        await RegisterAsync(signUpPage, email, StrongPassword);
        await RelioAppFixture.ClosePageAsync(signUpPage);
        (await GetStoredTimeZoneAsync(email)).Should().Be("Pacific/Pago_Pago");

        // ...and later signs in from a browser set to Kiritimati.
        var page = await fixture.NewPageAsync(timezoneId: "Pacific/Kiritimati");
        await LoginAndWaitForAppAsync(page, email, StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");

        var suggestion = page.Locator("[data-testid='settings-timezone-browser-suggestion']");
        await Expect(suggestion).ToContainTextAsync("Pacific/Kiritimati");
        (await GetStoredTimeZoneAsync(email)).Should().Be("Pacific/Pago_Pago", "a suggestion is never saved by itself");

        await page.Locator("[data-testid='settings-timezone-use-browser']").ClickAsync();
        await page.Locator("[data-testid='settings-timezone-save']").ClickAsync();
        await Expect(page.GetByText("Time zone saved")).ToBeVisibleAsync();
        await Expect(suggestion).ToHaveCountAsync(0);

        (await GetStoredTimeZoneAsync(email)).Should().Be("Pacific/Kiritimati");

        // After a reload the saved zone is shown and the browser matches it, so nothing is suggested.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");
        await Expect(page.GetByLabel("Time zone")).ToHaveValueAsync("Pacific/Kiritimati");
        await Expect(suggestion).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Typed_time_zone_can_be_saved()
    {
        var email = NewEmail("typed");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, email, StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");

        var field = page.GetByLabel("Time zone");
        await field.FillAsync("America/New_York");
        await field.PressAsync("Escape"); // close the suggestion list so it cannot cover the Save button.
        await page.Locator("[data-testid='settings-timezone-save']").ClickAsync();
        await Expect(page.GetByText("Time zone saved")).ToBeVisibleAsync();

        (await GetStoredTimeZoneAsync(email)).Should().Be("America/New_York");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task An_unknown_typed_time_zone_is_rejected_and_not_saved()
    {
        var email = NewEmail("badzone");
        var page = await fixture.NewPageAsync(timezoneId: "Pacific/Pago_Pago");
        await RegisterAsync(page, email, StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");

        var field = page.GetByLabel("Time zone");
        await field.FillAsync("Mars/Olympus_Mons");
        await field.PressAsync("Escape");
        await page.Locator("[data-testid='settings-timezone-save']").ClickAsync();

        await Expect(page.GetByText("Choose a time zone from the list, for example Europe/Rome.")).ToBeVisibleAsync();
        (await GetStoredTimeZoneAsync(email)).Should().Be("Pacific/Pago_Pago");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Theory]
    [InlineData("/Account/Manage/Email")]
    [InlineData("/Account/Manage/ChangePassword")]
    public async Task Manage_pages_require_sign_in(string path)
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync(path);

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Settings_links_point_to_the_manage_pages_and_open_them()
    {
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, NewEmail("links"), StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");

        await Expect(page.Locator("[data-testid='settings-change-email']")).ToHaveAttributeAsync("href", "/Account/Manage/Email");
        await Expect(page.Locator("[data-testid='settings-change-password']"))
            .ToHaveAttributeAsync("href", "/Account/Manage/ChangePassword");
        await Expect(page.Locator("[data-testid='settings-2fa-manage']"))
            .ToHaveAttributeAsync("href", "/Account/Manage/TwoFactorAuthentication");

        // From the interactive hub into a static SSR page: must be a real page load, since these
        // pages are excluded from interactive routing.
        await page.Locator("[data-testid='settings-change-password']").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Manage/ChangePassword$"));
        await Expect(page.Locator("[data-testid='manage-password-submit']")).ToBeVisibleAsync();
        // The shell (drawer) is gone: this is the static page, not the interactive router's Not Found.
        await Expect(page.Locator("aside.mud-drawer")).ToHaveCountAsync(0);
        await Expect(page.GetByText("Not Found")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Signed_in_email_in_the_app_bar_links_to_settings_on_phone()
    {
        var email = NewEmail("appbar");
        var page = await fixture.NewPageAsync(Viewports.Phone);
        await RegisterAsync(page, email, StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");

        var signedInAs = page.Locator("[data-testid='signed-in-as']");
        await Expect(signedInAs).ToHaveTextAsync(email);
        await Expect(signedInAs).ToHaveAttributeAsync("href", "/settings");

        await signedInAs.ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/settings$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    private async Task<string?> GetStoredTimeZoneAsync(string email)
    {
        var user = await GetUserAsync(fixture.App, email);
        user.Should().NotBeNull();

        using var scope = fixture.App.CreateRealScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Relio.Data.RelioDbContext>();
        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.OwnerId == user!.Id)
            .Select(p => p.TimeZoneId)
            .SingleOrDefaultAsync();
    }
}
