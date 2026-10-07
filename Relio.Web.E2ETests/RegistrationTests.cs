using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #15's acceptance criteria end to end, in a real browser against the real app
/// (see AGENTS.md "End-to-end tests"). <see cref="EmailConfirmationTests"/> covers the
/// <c>Email:Provider=Smtp</c> branch (confirmation required before sign-in) separately, since it
/// needs its own app instance with a test email sink.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class RegistrationTests(RelioAppFixture fixture)
{
    private const string ValidPassword = "Str0ng-Passw0rd!";

    [Fact]
    public async Task Registering_with_valid_data_starts_the_guide_and_lands_in_the_app()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("register");

        await RegisterAsync(page, email, ValidPassword);

        await Expect(page).ToHaveURLAsync(new Regex("/onboarding$"));
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(page.GetByRole(Microsoft.Playwright.AriaRole.Heading,
            new() { Name = "Get started", Exact = true })).ToBeVisibleAsync();
        await page.GetByTestId("onboarding-skip").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/$"));
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Weak_password_is_rejected_with_a_message()
    {
        var page = await fixture.NewPageAsync();

        await RegisterAsync(page, NewEmail("weak"), "short1");

        await Expect(page.Locator("[data-testid='register-error']")).ToContainTextAsync("at least 12 characters");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Register$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_without_leaking_more_than_necessary()
    {
        var email = NewEmail("dup");

        var firstPage = await fixture.NewPageAsync();
        await RegisterAsync(firstPage, email, ValidPassword);
        await Expect(firstPage).ToHaveURLAsync(new Regex("/onboarding$"));
        await RelioAppFixture.ClosePageAsync(firstPage);

        // A separate browser context (own cookies), not a sign-out on the same page: the first
        // account is now signed in, and signing it out again is not what this test is about -
        // registering the same email a second time must fail regardless of who else is signed in.
        var secondPage = await fixture.NewPageAsync();
        await RegisterAsync(secondPage, email, "An0ther-Str0ng-Pass!");

        await Expect(secondPage.Locator("[data-testid='register-error']"))
            .ToContainTextAsync("An account with this email already exists.");

        await RelioAppFixture.ClosePageAsync(secondPage);
    }

    [Fact]
    public async Task Time_zone_is_captured_from_the_browser_on_registration()
    {
        const string browserTimeZone = "Pacific/Kiritimati";
        var page = await fixture.NewPageAsync(timezoneId: browserTimeZone);
        var email = NewEmail("timezone");

        await RegisterAsync(page, email, ValidPassword);
        await Expect(page).ToHaveURLAsync(new Regex("/onboarding$"));

        using var scope = fixture.App.CreateRealScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();

        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        var profile = await dbContext.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerId == user!.Id);
        profile.Should().NotBeNull();
        profile!.TimeZoneId.Should().Be(browserTimeZone);

        await RelioAppFixture.ClosePageAsync(page);
    }

    private static string NewEmail(string label) => $"{label}-{Guid.NewGuid():N}@example.com";

    private static async Task RegisterAsync(Microsoft.Playwright.IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Register");
        await page.Locator("[data-testid='register-email']").FillAsync(email);
        await page.Locator("[data-testid='register-password']").FillAsync(password);
        await page.Locator("[data-testid='register-confirm-password']").FillAsync(password);
        await page.Locator("[data-testid='register-submit']").ClickAsync();
    }
}
