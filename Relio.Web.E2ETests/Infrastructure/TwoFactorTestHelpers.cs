using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data.Identity;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// Helpers for the two-factor authentication tests (issue #20). Two-factor authentication is only
/// ever turned on for a <i>fresh</i> user registered by the test itself - never the shared demo
/// user, which every other test signs in as and which would be locked behind a code nobody could
/// produce.
/// </summary>
public static class TwoFactorTestHelpers
{
    /// <summary>A user whose two-factor authentication is on, with the secrets the test needs.</summary>
    public sealed record TwoFactorUser(string Email, string Password, string Key, IReadOnlyList<string> RecoveryCodes);

    /// <summary>
    /// Registers a new user through the real Register page, then turns two-factor authentication on
    /// directly through the app's services (the setup page has its own tests), and returns the
    /// authenticator key and recovery codes. The registration's own session is discarded: turning
    /// two-factor authentication on rotates the security stamp.
    /// </summary>
    public static async Task<TwoFactorUser> CreateTwoFactorUserAsync(RelioAppFixture fixture, string prefix = "2fa")
    {
        var email = AccountTestHelpers.NewEmail(prefix);
        var registrationPage = await fixture.NewPageAsync();
        await AccountTestHelpers.RegisterAsync(registrationPage, email, AccountTestHelpers.StrongPassword);
        await RelioAppFixture.ClosePageAsync(registrationPage);

        var (key, codes) = await EnableTwoFactorAsync(fixture.App, email);
        return new TwoFactorUser(email, AccountTestHelpers.StrongPassword, key, codes);
    }

    /// <summary>Turns two-factor authentication on for an existing user and returns the key and recovery codes.</summary>
    public static async Task<(string Key, IReadOnlyList<string> RecoveryCodes)> EnableTwoFactorAsync(
        RelioWebAppFactory factory, string email)
    {
        using var scope = factory.CreateRealScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
        var user = (await userManager.FindByEmailAsync(email))!;

        (await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
        var key = (await userManager.GetAuthenticatorKeyAsync(user))!;
        var codes = await userManager.TurnOnTwoFactorAsync(user);
        codes.Should().NotBeNull();
        return (key, codes!);
    }

    /// <summary>Marks a user as disabled by an Administrator, directly through the app's services.</summary>
    public static async Task DisableAccountAsync(RelioWebAppFactory factory, string email)
    {
        using var scope = factory.CreateRealScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
        var user = (await userManager.FindByEmailAsync(email))!;
        user.IsDisabled = true;
        (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// Submits the real Login page and waits for the authenticator-code step (the page never gets as
    /// far as the app, so there is no <c>data-app-ready</c> to wait for).
    /// </summary>
    public static async Task SubmitPasswordStepAsync(IPage page, string email, string password, bool rememberMe = false)
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("[data-testid='login-email']").FillAsync(email);
        await page.Locator("[data-testid='login-password']").FillAsync(password);
        if (rememberMe)
        {
            await page.Locator("[data-testid='login-remember-me']").CheckAsync();
        }

        await page.Locator("[data-testid='login-submit']").ClickAsync();
        await page.Locator("[data-testid='login-2fa-code']").WaitForAsync();
    }

    /// <summary>Types <paramref name="code"/> into the authenticator-code step the page is showing and submits it.</summary>
    public static async Task SubmitAuthenticatorCodeAsync(IPage page, string code)
    {
        await page.Locator("[data-testid='login-2fa-code']").FillAsync(code);
        await page.Locator("[data-testid='login-2fa-submit']").ClickAsync();
    }

    /// <summary>Types <paramref name="code"/> into the recovery-code step the page is showing and submits it.</summary>
    public static async Task SubmitRecoveryCodeAsync(IPage page, string code)
    {
        await page.Locator("[data-testid='login-recovery-code']").FillAsync(code);
        await page.Locator("[data-testid='login-recovery-code-submit']").ClickAsync();
    }

    /// <summary>The code an authenticator app would show right now for <paramref name="key"/>.</summary>
    public static string CurrentCode(string key) => Totp.Compute(key, DateTimeOffset.UtcNow);

    /// <summary>
    /// Completes a whole sign-in (password, then the current authenticator code) and waits for the
    /// app's first interactive render.
    /// </summary>
    public static async Task SignInWithTwoFactorAsync(IPage page, TwoFactorUser user)
    {
        await SubmitPasswordStepAsync(page, user.Email, user.Password);
        await SubmitAuthenticatorCodeAsync(page, CurrentCode(user.Key));
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(user.Email);
    }
}
