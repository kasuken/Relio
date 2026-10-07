using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #18's change-password page end to end: the current password is required, the
/// session that changed it stays signed in, the old password stops working, and any other session
/// is signed out. Every test uses a fresh user (see <see cref="AccountTestHelpers"/>'s remarks).
/// </summary>
[Collection(RelioAppCollection.Name)]
public class ChangePasswordTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Changing_password_keeps_this_session_signed_in_and_only_the_new_password_works()
    {
        var email = NewEmail("pwchange");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, email, StrongPassword);

        await SubmitChangePasswordAsync(page, StrongPassword, OtherStrongPassword, OtherStrongPassword);

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Manage/ChangePassword\\?status=changed$"));
        await Expect(page.Locator("[data-testid='manage-password-status']")).ToBeVisibleAsync();

        // This session is still signed in...
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

        // ...and from now on only the new password signs in.
        await SignOutAsync(page);
        await LoginAsync(page, email, StrongPassword);
        await Expect(page.Locator("[data-testid='login-error']")).ToBeVisibleAsync();
        await LoginAndWaitForAppAsync(page, email, OtherStrongPassword);
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected()
    {
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, NewEmail("pwwrong"), StrongPassword);

        await SubmitChangePasswordAsync(page, "Not-My-Passw0rd!!", OtherStrongPassword, OtherStrongPassword);

        await Expect(page.Locator("[data-testid='manage-password-error']"))
            .ToHaveTextAsync("Your current password is incorrect.");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Manage/ChangePassword$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Weak_new_password_is_rejected()
    {
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, NewEmail("pwweak"), StrongPassword);

        await SubmitChangePasswordAsync(page, StrongPassword, "short1", "short1");

        await Expect(page.Locator("[data-testid='manage-password-error']")).ToContainTextAsync("at least 12 characters");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Mismatched_confirmation_is_rejected()
    {
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, NewEmail("pwmismatch"), StrongPassword);

        await SubmitChangePasswordAsync(page, StrongPassword, OtherStrongPassword, "Something-Else-Entirely1!");

        await Expect(page.GetByText("The password and confirmation do not match.")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Changing_password_signs_out_other_sessions()
    {
        // The application cookie only re-checks the security stamp every 30 minutes by default;
        // a zero interval makes every request re-check it, so "signed out elsewhere" is observable
        // in a test. See AGENTS.md "Account settings".
        var factory = CreateFactory(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero));
        try
        {
            var email = NewEmail("pwother");

            var contextA = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            var contextB = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            try
            {
                var pageA = await contextA.NewPageAsync();
                await RegisterAsync(pageA, email, StrongPassword);
                await RelioAppFixture.GotoAndWaitForInteractiveAsync(pageA, "/dashboard");
                await Expect(pageA.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

                // A second device signs in, then changes the password.
                var pageB = await contextB.NewPageAsync();
                await LoginAndWaitForAppAsync(pageB, email, StrongPassword);
                await SubmitChangePasswordAsync(pageB, StrongPassword, OtherStrongPassword, OtherStrongPassword);
                await Expect(pageB.Locator("[data-testid='manage-password-status']")).ToBeVisibleAsync();

                // The device that changed it stays signed in; the first device is signed out.
                await pageB.GotoAsync("/settings");
                await Expect(pageB.Locator("[data-testid='settings-current-email']")).ToHaveTextAsync(email);

                await pageA.GotoAsync("/settings");
                await Expect(pageA).ToHaveURLAsync(new Regex("/Account/Login"));
            }
            finally
            {
                await contextA.CloseAsync();
                await contextB.CloseAsync();
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static async Task SubmitChangePasswordAsync(IPage page, string current, string newPassword, string confirm)
    {
        await page.GotoAsync("/Account/Manage/ChangePassword");
        await page.Locator("[data-testid='manage-password-current']").FillAsync(current);
        await page.Locator("[data-testid='manage-password-new']").FillAsync(newPassword);
        await page.Locator("[data-testid='manage-password-confirm']").FillAsync(confirm);
        await page.Locator("[data-testid='manage-password-submit']").ClickAsync();
    }
}
