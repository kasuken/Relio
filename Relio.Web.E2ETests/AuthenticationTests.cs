using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Relio.Data.Seeding;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #16's acceptance criteria end to end, in a real browser against the real app (see
/// AGENTS.md "End-to-end tests"): valid/invalid sign-in, "Remember me"'s persistent-vs-session
/// cookie, session persistence across a reload and a new tab, sign-out, open-redirect protection,
/// and account lockout after repeated failed attempts.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class AuthenticationTests(RelioAppFixture fixture)
{
    private const string ValidPassword = "Str0ng-Passw0rd!";

    [Fact]
    public async Task Unauthenticated_visit_redirects_to_login()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/");

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Demo_user_can_sign_in_and_sees_the_app()
    {
        var page = await fixture.NewPageAsync();

        await RelioAppFixture.SignInAsDemoAsync(page);

        await Expect(page).ToHaveURLAsync(new Regex("/$"));
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(DemoDataSeeder.DemoEmail);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Invalid_credentials_show_a_generic_error()
    {
        var (email, _) = await RegisterNewUserAsync();

        var page = await fixture.NewPageAsync();
        await LoginAsync(page, email, "Totally-Wrong-Password1!");

        await Expect(page.Locator("[data-testid='login-error']")).ToHaveTextAsync("Email or password is incorrect.");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Open_redirect_return_url_is_ignored()
    {
        var (email, password) = await RegisterNewUserAsync();

        var page = await fixture.NewPageAsync();
        await page.GotoAsync("/Account/Login?returnUrl=" + Uri.EscapeDataString("https://evil.example/steal"));
        await page.Locator("[data-testid='login-email']").FillAsync(email);
        await page.Locator("[data-testid='login-password']").FillAsync(password);
        await page.Locator("[data-testid='login-submit']").ClickAsync();
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

        // Falls back to the app's own dashboard, never navigates to the attacker-supplied host.
        await Expect(page).ToHaveURLAsync(new Regex("^" + Regex.Escape(fixture.BaseUrl) + "/$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Remember_me_issues_a_persistent_cookie()
    {
        var (email, password) = await RegisterNewUserAsync();

        var page = await fixture.NewPageAsync();
        await page.GotoAsync("/Account/Login");
        await page.Locator("[data-testid='login-email']").FillAsync(email);
        await page.Locator("[data-testid='login-password']").FillAsync(password);
        await page.Locator("[data-testid='login-remember-me']").CheckAsync();
        await page.Locator("[data-testid='login-submit']").ClickAsync();
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

        var authCookie = await GetAuthCookieAsync(page);
        authCookie.Expires.Should().BeGreaterThan(0, "'Remember me' should issue a persistent cookie with an Expires attribute");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Without_remember_me_the_cookie_is_session_only()
    {
        var (email, password) = await RegisterNewUserAsync();

        var page = await fixture.NewPageAsync();
        await LoginAsync(page, email, password);

        var authCookie = await GetAuthCookieAsync(page);
        authCookie.Expires.Should().Be(-1, "without 'Remember me' the cookie should be session-only (no Expires attribute)");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Session_persists_across_reload_and_a_new_tab()
    {
        var (email, password) = await RegisterNewUserAsync();

        var page = await fixture.NewPageAsync();
        await LoginAsync(page, email, password);

        await page.ReloadAsync();
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

        // A second page in the SAME browser context: cookies are shared within a context, so a
        // brand new tab should already be signed in, with no second login.
        var secondTab = await page.Context.NewPageAsync();
        await secondTab.GotoAsync("/");
        await secondTab.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(secondTab.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);
        await secondTab.CloseAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Signing_out_redirects_to_login_and_blocks_back_navigation()
    {
        var (email, password) = await RegisterNewUserAsync();

        var page = await fixture.NewPageAsync();
        await LoginAsync(page, email, password);

        await page.Locator("[data-testid='sign-out']").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login$"));

        // The back button must not reveal the previously-authenticated page (Cache-Control:
        // no-store on authenticated responses - see Program.cs) - going back lands on the login
        // page again, not a cached dashboard.
        await page.GoBackAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));
        await Expect(page.Locator("[data-testid='signed-in-as']")).Not.ToBeVisibleAsync();

        // And the protected page itself is gone for good until signing in again.
        await page.GotoAsync("/");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Repeated_failed_attempts_lock_the_account_even_with_the_correct_password()
    {
        // A freshly registered user, never the demo account, so this test's lockout cannot affect
        // any other test signing in as the demo user.
        var (email, password) = await RegisterNewUserAsync();

        var page = await fixture.NewPageAsync();

        string? lastErrorText = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await page.GotoAsync("/Account/Login");
            await page.Locator("[data-testid='login-email']").FillAsync(email);
            await page.Locator("[data-testid='login-password']").FillAsync("Totally-Wrong-Password1!");
            await page.Locator("[data-testid='login-submit']").ClickAsync();
            var errorLocator = page.Locator("[data-testid='login-error']");
            await errorLocator.WaitForAsync();
            lastErrorText = await errorLocator.TextContentAsync();
        }

        lastErrorText.Should().Contain("locked");

        // The 6th attempt, with the CORRECT password, is still locked out.
        await LoginAsync(page, email, password);
        await Expect(page.Locator("[data-testid='login-error']")).ToContainTextAsync("locked");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    private static async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("[data-testid='login-email']").FillAsync(email);
        await page.Locator("[data-testid='login-password']").FillAsync(password);
        await page.Locator("[data-testid='login-submit']").ClickAsync();
    }

    private static async Task<BrowserContextCookiesResult> GetAuthCookieAsync(IPage page)
    {
        var cookies = await page.Context.CookiesAsync();
        return cookies.Single(c => c.Name.Contains("Identity.Application", StringComparison.Ordinal));
    }

    /// <summary>
    /// Registers a brand new account (own, isolated browser context) through the real register
    /// page and returns its email/password - every test above that signs in deliberately uses a
    /// fresh account rather than the shared demo user, so one test's failed attempts/lockout can
    /// never affect another test.
    /// </summary>
    private async Task<(string Email, string Password)> RegisterNewUserAsync()
    {
        var email = $"login-{Guid.NewGuid():N}@example.com";

        var page = await fixture.NewPageAsync();
        await page.GotoAsync("/Account/Register");
        await page.Locator("[data-testid='register-email']").FillAsync(email);
        await page.Locator("[data-testid='register-password']").FillAsync(ValidPassword);
        await page.Locator("[data-testid='register-confirm-password']").FillAsync(ValidPassword);
        await page.Locator("[data-testid='register-submit']").ClickAsync();
        await page.Locator("[data-testid='onboarding-page']").WaitForAsync();
        await RelioAppFixture.ClosePageAsync(page);

        return (email, ValidPassword);
    }
}
