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
/// With static assets served to signed-out visitors, <c>blazor.web.js</c> loads on the static SSR
/// account pages, so clicking a link between them is an enhanced navigation (a fetch + DOM patch)
/// instead of a full page load. These tests click through the real links, instead of
/// <c>GotoAsync</c>-ing each page, to prove the account flows still work that way.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class AnonymousEnhancedNavigationTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Registering_after_following_the_create_account_link_still_captures_the_time_zone()
    {
        const string browserTimeZone = "Pacific/Kiritimati";
        var page = await fixture.NewPageAsync(timezoneId: browserTimeZone);
        var email = NewEmail("enhanced-register");

        await page.GotoAsync("/Account/Login");
        await page.GetByRole(AriaRole.Link, new() { Name = "Create an account" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Register$"));

        // Register.razor's inline script fills the hidden time zone field; it has to run for an
        // enhanced navigation to the page too, not only for a full page load.
        await Expect(page.Locator("#register-timezone-input")).ToHaveValueAsync(browserTimeZone);

        await page.Locator("[data-testid='register-email']").FillAsync(email);
        await page.Locator("[data-testid='register-password']").FillAsync(StrongPassword);
        await page.Locator("[data-testid='register-confirm-password']").FillAsync(StrongPassword);
        await page.Locator("[data-testid='register-submit']").ClickAsync();
        await page.Locator("[data-testid='register-confirmation-heading']").WaitForAsync();

        // The confirmation page's "continue" button is an enhanced navigation to an interactive page.
        await page.Locator("[data-testid='register-confirmation-continue']").ClickAsync();
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

        using var scope = fixture.App.CreateRealScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>().FindByEmailAsync(email);
        var profile = await scope.ServiceProvider.GetRequiredService<RelioDbContext>().UserProfiles
            .AsNoTracking().FirstAsync(p => p.OwnerId == user!.Id);
        profile.TimeZoneId.Should().Be(browserTimeZone);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Login_forgot_password_and_back_via_links_then_sign_in_works()
    {
        var email = NewEmail("enhanced-login");
        var registerPage = await fixture.NewPageAsync();
        await RegisterAsync(registerPage, email, StrongPassword);
        await RelioAppFixture.ClosePageAsync(registerPage);

        var page = await fixture.NewPageAsync();
        await page.GotoAsync("/Account/Login");

        await page.Locator("[data-testid='login-forgot-password']").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/Account/ForgotPassword$"));
        await page.Locator("[data-testid='forgot-password-email']").FillAsync(email);
        await page.Locator("[data-testid='forgot-password-submit']").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/Account/ForgotPasswordConfirmation$"));

        await page.GetByRole(AriaRole.Link, new() { Name = "Back to sign in" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login$"));

        await page.Locator("[data-testid='login-email']").FillAsync(email);
        await page.Locator("[data-testid='login-password']").FillAsync(StrongPassword);
        await page.Locator("[data-testid='login-submit']").ClickAsync();
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

        await RelioAppFixture.ClosePageAsync(page);
    }
}
