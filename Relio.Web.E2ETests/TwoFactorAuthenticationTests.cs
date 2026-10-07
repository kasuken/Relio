using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;
using static Relio.Web.E2ETests.Infrastructure.TwoFactorTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #20 end to end: turning two-factor authentication on with an authenticator app,
/// signing in with a code or a recovery code, the shared lockout budget, and turning it off. The
/// codes the "app" would show come from <see cref="Totp"/>. Every test uses a fresh user - never the
/// demo account (see <see cref="TwoFactorTestHelpers"/>'s remarks).
/// </summary>
[Collection(RelioAppCollection.Name)]
public partial class TwoFactorAuthenticationTests(RelioAppFixture fixture)
{
    [GeneratedRegex("^[2-9BCDFGHJKMNPQRTVWXY]{5}-[2-9BCDFGHJKMNPQRTVWXY]{5}$")]
    private static partial Regex RecoveryCodeShape();

    private static async Task<string> ReadSharedKeyAsync(IPage page)
    {
        var shown = await page.Locator("[data-testid='manage-2fa-shared-key']").TextContentAsync();
        return shown!.Replace(" ", string.Empty).ToUpperInvariant();
    }

    private static async Task<IReadOnlyList<string>> ReadRecoveryCodesAsync(IPage page)
    {
        var items = page.Locator("[data-testid='recovery-code']");
        var texts = new List<string>();
        for (var i = 0; i < await items.CountAsync(); i++)
        {
            texts.Add((await items.Nth(i).TextContentAsync())!.Trim());
        }

        return texts;
    }

    private static async Task TurnOnThroughThePagesAsync(IPage page, string password)
    {
        await page.GotoAsync("/Account/Manage/EnableAuthenticator");
        var key = await ReadSharedKeyAsync(page);
        await page.Locator("[data-testid='manage-2fa-code']").FillAsync(Totp.Compute(key, DateTimeOffset.UtcNow));
        await page.Locator("[data-testid='manage-2fa-password']").FillAsync(password);
        await page.Locator("[data-testid='manage-2fa-verify']").ClickAsync();
        await page.Locator("[data-testid='manage-2fa-done']").WaitForAsync();
    }

    [Fact]
    public async Task User_can_turn_on_two_factor_with_an_authenticator_app()
    {
        var email = NewEmail("2fa-setup");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, email, StrongPassword);

        // Settings shows it off, with a link to set it up.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");
        await Expect(page.Locator("[data-testid='settings-2fa-state']")).ToHaveTextAsync("Off");
        await Expect(page.Locator("[data-testid='settings-2fa-manage']")).ToHaveTextAsync("Set up two-factor authentication");

        await page.GotoAsync("/Account/Manage/EnableAuthenticator");

        // The QR code is drawn (inline SVG path) and the same secret is offered as a typed key.
        await Expect(page.Locator("[data-testid='manage-2fa-qr']")).ToBeVisibleAsync();
        (await page.Locator("[data-testid='manage-2fa-qr'] path.rl-qr-ink").GetAttributeAsync("d")).Should().NotBeNullOrWhiteSpace();
        var key = await ReadSharedKeyAsync(page);
        (await GetAuthenticatorKeyAsync(email)).Should().Be(key);
        (await page.Locator("[data-testid='manage-2fa-otpauth-link']").GetAttributeAsync("href"))
            .Should().StartWith("otpauth://totp/Relio:").And.Contain($"secret={key}");

        // Nothing is on yet.
        (await GetUserAsync(fixture.App, email))!.TwoFactorEnabled.Should().BeFalse();

        await page.Locator("[data-testid='manage-2fa-code']").FillAsync(Totp.Compute(key, DateTimeOffset.UtcNow));
        await page.Locator("[data-testid='manage-2fa-password']").FillAsync(StrongPassword);
        await page.Locator("[data-testid='manage-2fa-verify']").ClickAsync();

        await Expect(page.Locator("[data-testid='manage-2fa-status']")).ToContainTextAsync("Two-factor authentication is on");
        var codes = await ReadRecoveryCodesAsync(page);
        codes.Should().HaveCount(10).And.OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => RecoveryCodeShape().IsMatch(c));
        (await GetUserAsync(fixture.App, email))!.TwoFactorEnabled.Should().BeTrue();

        // The status page confirms it - and never shows the codes again.
        await Expect(page.Locator("[data-testid='manage-2fa-done']"))
            .ToHaveAttributeAsync("href", "/Account/Manage/TwoFactorAuthentication");
        await page.GotoAsync("/Account/Manage/TwoFactorAuthentication");
        await Expect(page.Locator("[data-testid='manage-2fa-state']")).ToHaveTextAsync("On");
        await Expect(page.Locator("[data-testid='manage-2fa-recovery-codes-left']")).ToContainTextAsync("10 recovery codes");
        await Expect(page.Locator("[data-testid='recovery-code']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='manage-2fa-recovery-codes-low']")).ToHaveCountAsync(0);

        // This session survived the security stamp rotation, and settings now shows it on.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");
        await Expect(page.Locator("[data-testid='settings-2fa-state']")).ToHaveTextAsync("On");
        await Expect(page.Locator("[data-testid='settings-2fa-manage']")).ToHaveTextAsync("Manage two-factor authentication");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Turning_on_needs_the_current_password_and_a_valid_code()
    {
        var email = NewEmail("2fa-guard");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, email, StrongPassword);
        await page.GotoAsync("/Account/Manage/EnableAuthenticator");
        var key = await ReadSharedKeyAsync(page);

        // A valid code but the wrong password.
        await page.Locator("[data-testid='manage-2fa-code']").FillAsync(Totp.Compute(key, DateTimeOffset.UtcNow));
        await page.Locator("[data-testid='manage-2fa-password']").FillAsync(OtherStrongPassword);
        await page.Locator("[data-testid='manage-2fa-verify']").ClickAsync();
        await Expect(page.Locator("[data-testid='manage-2fa-error']")).ToHaveTextAsync("Your current password is incorrect.");

        // The right password but a wrong code.
        await page.GotoAsync("/Account/Manage/EnableAuthenticator");
        await page.Locator("[data-testid='manage-2fa-code']").FillAsync(Totp.CreateWrongCode(key, DateTimeOffset.UtcNow));
        await page.Locator("[data-testid='manage-2fa-password']").FillAsync(StrongPassword);
        await page.Locator("[data-testid='manage-2fa-verify']").ClickAsync();
        await Expect(page.Locator("[data-testid='manage-2fa-error']")).ToContainTextAsync("That code is incorrect or has expired");

        // And something that is not a code at all.
        await page.GotoAsync("/Account/Manage/EnableAuthenticator");
        await page.Locator("[data-testid='manage-2fa-code']").FillAsync("not a code");
        await page.Locator("[data-testid='manage-2fa-password']").FillAsync(StrongPassword);
        await page.Locator("[data-testid='manage-2fa-verify']").ClickAsync();
        await Expect(page.Locator("[data-testid='manage-2fa-error']")).ToContainTextAsync("That code is incorrect or has expired");

        (await GetUserAsync(fixture.App, email))!.TwoFactorEnabled.Should().BeFalse("nothing was turned on");

        // Typing the key as shown (spaces, lower case) and the code with a space still works.
        var code = Totp.Compute(key, DateTimeOffset.UtcNow);
        await page.GotoAsync("/Account/Manage/EnableAuthenticator");
        await page.Locator("[data-testid='manage-2fa-code']").FillAsync($"{code[..3]} {code[3..]}");
        await page.Locator("[data-testid='manage-2fa-password']").FillAsync(StrongPassword);
        await page.Locator("[data-testid='manage-2fa-verify']").ClickAsync();
        await Expect(page.Locator("[data-testid='manage-2fa-done']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Sign_in_requires_a_code_once_two_factor_is_on()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();

        await SubmitPasswordStepAsync(page, user.Email, user.Password);

        // Only the short-lived "who is signing in" cookie exists: no session yet.
        await Expect(page).ToHaveURLAsync(new Regex("/Account/LoginWith2fa"));
        var cookieNames = (await page.Context.CookiesAsync()).Select(c => c.Name).ToList();
        cookieNames.Should().Contain(n => n.Contains("Identity.TwoFactorUserId", StringComparison.Ordinal));
        cookieNames.Should().NotContain(n => n.Contains("Identity.Application", StringComparison.Ordinal));
        await page.GotoAsync("/dashboard");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

        // The second step still works afterwards, and completes the sign-in.
        await page.GotoAsync("/Account/LoginWith2fa");
        await SubmitAuthenticatorCodeAsync(page, CurrentCode(user.Key));
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(user.Email);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Wrong_codes_are_rejected_and_repeated_failures_lock_the_account()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();
        await SubmitPasswordStepAsync(page, user.Email, user.Password);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await page.GotoAsync("/Account/LoginWith2fa");
            await SubmitAuthenticatorCodeAsync(page, Totp.CreateWrongCode(user.Key, DateTimeOffset.UtcNow));
            await Expect(page.Locator("[data-testid='login-2fa-error']")).ToContainTextAsync("incorrect or has expired");
        }

        // A typo that is not even six digits is answered without counting as an attempt.
        await page.GotoAsync("/Account/LoginWith2fa");
        await SubmitAuthenticatorCodeAsync(page, "12ab");
        await Expect(page.Locator("[data-testid='login-2fa-error']")).ToHaveTextAsync("Enter the 6-digit code from your authenticator app.");

        // The fifth failure (the password step counted none) locks the account...
        await page.GotoAsync("/Account/LoginWith2fa");
        await SubmitAuthenticatorCodeAsync(page, Totp.CreateWrongCode(user.Key, DateTimeOffset.UtcNow));
        await Expect(page.Locator("[data-testid='login-2fa-error']")).ToContainTextAsync("locked");

        // ...and from then on even the right code is refused.
        await page.GotoAsync("/Account/LoginWith2fa");
        await SubmitAuthenticatorCodeAsync(page, CurrentCode(user.Key));
        await Expect(page.Locator("[data-testid='login-2fa-error']")).ToContainTextAsync("locked");
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Recovery_codes_work_once_each()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();

        await SubmitPasswordStepAsync(page, user.Email, user.Password);
        await page.Locator("[data-testid='login-2fa-use-recovery-code']").ClickAsync();
        await SubmitRecoveryCodeAsync(page, user.RecoveryCodes[0]);
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(user.Email);
        await SignOutAsync(page);

        // The same code again: refused.
        await SubmitPasswordStepAsync(page, user.Email, user.Password);
        await page.GotoAsync("/Account/LoginWithRecoveryCode");
        await SubmitRecoveryCodeAsync(page, user.RecoveryCodes[0]);
        await Expect(page.Locator("[data-testid='login-recovery-code-error']"))
            .ToContainTextAsync("incorrect or has already been used");

        // Another code, typed in lower case with a leading space: accepted.
        await page.GotoAsync("/Account/LoginWithRecoveryCode");
        await SubmitRecoveryCodeAsync(page, " " + user.RecoveryCodes[1].ToLowerInvariant());
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

        // Two of the ten are used up.
        await page.GotoAsync("/Account/Manage/TwoFactorAuthentication");
        await Expect(page.Locator("[data-testid='manage-2fa-recovery-codes-left']")).ToContainTextAsync("8 recovery codes");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Repeated_wrong_recovery_codes_lock_the_account()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();
        await SubmitPasswordStepAsync(page, user.Email, user.Password);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await page.GotoAsync("/Account/LoginWithRecoveryCode");
            await SubmitRecoveryCodeAsync(page, "AAAAA-BBBBB");
            await Expect(page.Locator("[data-testid='login-recovery-code-error']")).ToContainTextAsync("incorrect or has already been used");
        }

        await page.GotoAsync("/Account/LoginWithRecoveryCode");
        await SubmitRecoveryCodeAsync(page, "AAAAA-BBBBB");
        await Expect(page.Locator("[data-testid='login-recovery-code-error']")).ToContainTextAsync("locked");

        // A genuine code no longer helps while locked out - and is not used up by the attempt.
        await page.GotoAsync("/Account/LoginWithRecoveryCode");
        await SubmitRecoveryCodeAsync(page, user.RecoveryCodes[0]);
        await Expect(page.Locator("[data-testid='login-recovery-code-error']")).ToContainTextAsync("locked");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Return_url_and_remember_me_survive_the_two_factor_step()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();

        // An anonymous visit to a protected page is bounced to the login page with a return URL.
        await page.GotoAsync("/people");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));
        await page.Locator("[data-testid='login-email']").FillAsync(user.Email);
        await page.Locator("[data-testid='login-password']").FillAsync(user.Password);
        await page.Locator("[data-testid='login-remember-me']").CheckAsync();
        await page.Locator("[data-testid='login-submit']").ClickAsync();
        await page.Locator("[data-testid='login-2fa-code']").WaitForAsync();

        await SubmitAuthenticatorCodeAsync(page, CurrentCode(user.Key));
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/people$"));
        var authCookie = (await page.Context.CookiesAsync()).Single(c => c.Name.Contains("Identity.Application", StringComparison.Ordinal));
        authCookie.Expires.Should().BeGreaterThan(0, "'Remember me' chosen at the password step still issues a persistent cookie");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Without_remember_me_the_two_factor_sign_in_cookie_is_session_only()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();

        await SignInWithTwoFactorAsync(page, user);

        var authCookie = (await page.Context.CookiesAsync()).Single(c => c.Name.Contains("Identity.Application", StringComparison.Ordinal));
        authCookie.Expires.Should().Be(-1);
        (await page.Context.CookiesAsync()).Should().NotContain(c => c.Name.Contains("TwoFactorRememberMe", StringComparison.Ordinal),
            "Relio never remembers a device");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Two_factor_step_ignores_an_unsafe_return_url()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();
        await SubmitPasswordStepAsync(page, user.Email, user.Password);

        // The query string is editable by anyone: the second step validates it again.
        await page.GotoAsync("/Account/LoginWith2fa?returnUrl=" + Uri.EscapeDataString("https://evil.example/steal"));
        await SubmitAuthenticatorCodeAsync(page, CurrentCode(user.Key));
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

        await Expect(page).ToHaveURLAsync(new Regex("^" + Regex.Escape(fixture.BaseUrl) + "/dashboard$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task User_can_turn_off_two_factor()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();
        await SignInWithTwoFactorAsync(page, user);

        await page.GotoAsync("/Account/Manage/Disable2fa");
        await page.Locator("[data-testid='manage-2fa-disable-password']").FillAsync(OtherStrongPassword);
        await page.Locator("[data-testid='manage-2fa-disable-submit']").ClickAsync();
        await Expect(page.Locator("[data-testid='manage-2fa-error']")).ToHaveTextAsync("Your current password is incorrect.");
        (await GetUserAsync(fixture.App, user.Email))!.TwoFactorEnabled.Should().BeTrue("a wrong password changes nothing");

        await page.Locator("[data-testid='manage-2fa-disable-password']").FillAsync(user.Password);
        await page.Locator("[data-testid='manage-2fa-disable-submit']").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Manage/TwoFactorAuthentication\\?status=disabled$"));
        await Expect(page.Locator("[data-testid='manage-2fa-status']")).ToContainTextAsync("Two-factor authentication is off");
        await Expect(page.Locator("[data-testid='manage-2fa-state']")).ToHaveTextAsync("Off");
        (await GetUserAsync(fixture.App, user.Email))!.TwoFactorEnabled.Should().BeFalse();
        (await GetAuthenticatorKeyAsync(user.Email)).Should().NotBe(user.Key, "the old app's key is gone for good");

        // This session stays signed in...
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(user.Email);

        // ...and the next sign-in only asks for the password.
        await SignOutAsync(page);
        await LoginAndWaitForAppAsync(page, user.Email, user.Password);
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(user.Email);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Resetting_requires_setting_up_again()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();
        await SignInWithTwoFactorAsync(page, user);

        await page.GotoAsync("/Account/Manage/ResetAuthenticator");
        await page.Locator("[data-testid='manage-2fa-reset-password']").FillAsync(user.Password);
        await page.Locator("[data-testid='manage-2fa-reset-submit']").ClickAsync();

        // Straight into setup, with a new key, and two-factor authentication is off until it is done.
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Manage/EnableAuthenticator\\?status=reset$"));
        await Expect(page.Locator("[data-testid='manage-2fa-reset-status']")).ToBeVisibleAsync();
        var newKey = await ReadSharedKeyAsync(page);
        newKey.Should().NotBe(user.Key);
        (await GetUserAsync(fixture.App, user.Email))!.TwoFactorEnabled.Should().BeFalse();

        await page.Locator("[data-testid='manage-2fa-code']").FillAsync(Totp.Compute(newKey, DateTimeOffset.UtcNow));
        await page.Locator("[data-testid='manage-2fa-password']").FillAsync(user.Password);
        await page.Locator("[data-testid='manage-2fa-verify']").ClickAsync();
        await Expect(page.Locator("[data-testid='manage-2fa-status']")).ToContainTextAsync("is on");
        (await ReadRecoveryCodesAsync(page)).Should().HaveCount(10);

        // The new app signs in; the old one no longer does.
        await SignOutAsync(page);
        await SubmitPasswordStepAsync(page, user.Email, user.Password);
        await SubmitAuthenticatorCodeAsync(page, Totp.Compute(user.Key, DateTimeOffset.UtcNow));
        await Expect(page.Locator("[data-testid='login-2fa-error']")).ToContainTextAsync("incorrect or has expired");
        await page.GotoAsync("/Account/LoginWith2fa");
        await SubmitAuthenticatorCodeAsync(page, Totp.Compute(newKey, DateTimeOffset.UtcNow));
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Generating_new_recovery_codes_invalidates_the_old_ones()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();
        await SignInWithTwoFactorAsync(page, user);

        await page.GotoAsync("/Account/Manage/GenerateRecoveryCodes");
        await page.Locator("[data-testid='manage-2fa-codes-password']").FillAsync(OtherStrongPassword);
        await page.Locator("[data-testid='manage-2fa-codes-submit']").ClickAsync();
        await Expect(page.Locator("[data-testid='manage-2fa-error']")).ToHaveTextAsync("Your current password is incorrect.");

        await page.Locator("[data-testid='manage-2fa-codes-password']").FillAsync(user.Password);
        await page.Locator("[data-testid='manage-2fa-codes-submit']").ClickAsync();
        await Expect(page.Locator("[data-testid='manage-2fa-status']")).ToContainTextAsync("New recovery codes created");
        var newCodes = await ReadRecoveryCodesAsync(page);
        newCodes.Should().HaveCount(10).And.NotIntersectWith(user.RecoveryCodes);

        // An old code is dead, a new one works.
        await SignOutAsync(page);
        await SubmitPasswordStepAsync(page, user.Email, user.Password);
        await page.GotoAsync("/Account/LoginWithRecoveryCode");
        await SubmitRecoveryCodeAsync(page, user.RecoveryCodes[0]);
        await Expect(page.Locator("[data-testid='login-recovery-code-error']")).ToContainTextAsync("incorrect or has already been used");
        await page.GotoAsync("/Account/LoginWithRecoveryCode");
        await SubmitRecoveryCodeAsync(page, newCodes[0]);
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Running_low_on_recovery_codes_is_called_out_gently()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();

        // Use up seven of the ten codes through the real sign-in page.
        for (var i = 0; i < 7; i++)
        {
            await SubmitPasswordStepAsync(page, user.Email, user.Password);
            await page.GotoAsync("/Account/LoginWithRecoveryCode");
            await SubmitRecoveryCodeAsync(page, user.RecoveryCodes[i]);
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            await SignOutAsync(page);
        }

        await SignInWithTwoFactorAsync(page, user);
        await page.GotoAsync("/Account/Manage/TwoFactorAuthentication");
        await Expect(page.Locator("[data-testid='manage-2fa-recovery-codes-left']")).ToContainTextAsync("3 recovery codes");
        await Expect(page.Locator("[data-testid='manage-2fa-recovery-codes-low']")).ToContainTextAsync("You only have 3 recovery codes left");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");
        await Expect(page.Locator("[data-testid='settings-2fa-recovery-codes-low']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Theory]
    [InlineData("/Account/LoginWith2fa", "login-2fa-expired", "login-2fa-code")]
    [InlineData("/Account/LoginWithRecoveryCode", "login-recovery-code-expired", "login-recovery-code")]
    public async Task Two_factor_steps_without_a_pending_sign_in_show_the_expired_message(
        string path, string expiredTestId, string fieldTestId)
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync(path);

        await Expect(page.Locator($"[data-testid='{expiredTestId}']")).ToContainTextAsync("Your sign-in timed out");
        await Expect(page.Locator($"[data-testid='{fieldTestId}']")).Not.ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Theory]
    [InlineData("/Account/Manage/TwoFactorAuthentication")]
    [InlineData("/Account/Manage/EnableAuthenticator")]
    [InlineData("/Account/Manage/GenerateRecoveryCodes")]
    [InlineData("/Account/Manage/Disable2fa")]
    [InlineData("/Account/Manage/ResetAuthenticator")]
    public async Task Manage_two_factor_pages_require_sign_in(string path)
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync(path);

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Theory]
    [InlineData("/Account/Manage/GenerateRecoveryCodes")]
    [InlineData("/Account/Manage/Disable2fa")]
    [InlineData("/Account/Manage/ResetAuthenticator")]
    public async Task Pages_that_change_an_active_setup_send_someone_without_one_elsewhere(string path)
    {
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, NewEmail("2fa-off"), StrongPassword);

        await page.GotoAsync(path);

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Manage/(TwoFactorAuthentication|EnableAuthenticator)$"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Setup_is_not_offered_again_while_two_factor_is_on()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();
        await SignInWithTwoFactorAsync(page, user);

        await page.GotoAsync("/Account/Manage/EnableAuthenticator");

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Manage/TwoFactorAuthentication$"));
        (await GetAuthenticatorKeyAsync(user.Email)).Should().Be(user.Key, "visiting setup must not replace the working key");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task A_disabled_account_never_reaches_the_code_step()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        await DisableAccountAsync(fixture.App, user.Email);
        var page = await fixture.NewPageAsync();

        await LoginAsync(page, user.Email, user.Password);

        await Expect(page.Locator("[data-testid='login-error']")).ToContainTextAsync("This account has been disabled");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login$"));
        (await page.Context.CookiesAsync()).Should().NotContain(c => c.Name.Contains("Identity.TwoFactorUserId", StringComparison.Ordinal));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task An_account_disabled_during_the_code_step_is_refused()
    {
        var user = await CreateTwoFactorUserAsync(fixture);
        var page = await fixture.NewPageAsync();
        await SubmitPasswordStepAsync(page, user.Email, user.Password);

        // An administrator disables the account between the password and the code.
        await DisableAccountAsync(fixture.App, user.Email);
        await SubmitAuthenticatorCodeAsync(page, CurrentCode(user.Key));

        await Expect(page.Locator("[data-testid='login-2fa-error']")).ToContainTextAsync("This account has been disabled");
        (await page.Context.CookiesAsync()).Should().NotContain(c => c.Name.Contains("Identity.Application", StringComparison.Ordinal));
        await page.GotoAsync("/dashboard");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    private async Task<string?> GetAuthenticatorKeyAsync(string email)
    {
        using var scope = fixture.App.CreateRealScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Relio.Data.Identity.RelioUser>>();
        var user = (await userManager.FindByEmailAsync(email))!;
        return await userManager.GetAuthenticatorKeyAsync(user);
    }
}
