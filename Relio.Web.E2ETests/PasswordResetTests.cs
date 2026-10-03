using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Relio.Data.Identity;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #17's acceptance criteria end to end: forgot-password/reset-password with
/// <c>Email:Provider=Smtp</c> (captured by <see cref="TestEmailSink"/>, the same pattern
/// <see cref="EmailConfirmationTests"/> uses), the identical confirmation message whether or not
/// the submitted email matches an account, a single-use reset link (reusing it after a successful
/// reset fails - see <see cref="Relio.Web.Components.Account.Pages.ResetPassword"/>'s remarks on
/// the security stamp rotation), an expiring link (a short <c>Account:PasswordReset:TokenLifespan</c>
/// in its own variant app), and the "unavailable" note shown on the shared fixture's app, which
/// runs with the default <c>Email:Provider=None</c>.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class PasswordResetTests(RelioAppFixture fixture)
{
    private const string OriginalPassword = "Str0ng-Passw0rd!";
    private const string NewPassword = "Even-Str0nger-Pass1!";

    [Fact]
    public async Task Forgot_password_shows_the_unavailable_note_when_no_email_provider_is_configured()
    {
        // The shared fixture's app runs with no Email:Provider configured (the default) - see
        // RelioWebAppFactory's remarks - so this needs no variant app of its own.
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/Account/ForgotPassword");

        await Expect(page.Locator("[data-testid='forgot-password-unavailable-note']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Unknown_email_shows_the_same_confirmation_and_sends_nothing()
    {
        var factory = CreateSmtpFactory();
        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            var page = await context.NewPageAsync();
            try
            {
                await SubmitForgotPasswordAsync(page, $"nobody-{Guid.NewGuid():N}@example.com");

                await Expect(page.Locator("[data-testid='forgot-password-confirmation-heading']"))
                    .ToHaveTextAsync("Check your email");

                using var scope = factory.CreateRealScope();
                scope.ServiceProvider.GetRequiredService<TestEmailSink>().LastPasswordResetLink.Should().BeNull();
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Full_reset_flow_issues_a_single_use_link_and_signs_in_with_the_new_password()
    {
        var factory = CreateSmtpFactory();
        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            var page = await context.NewPageAsync();
            try
            {
                var email = $"reset-{Guid.NewGuid():N}@example.com";
                await RegisterAndConfirmAsync(page, factory, email, OriginalPassword);

                await SubmitForgotPasswordAsync(page, email);
                await Expect(page.Locator("[data-testid='forgot-password-confirmation-heading']"))
                    .ToHaveTextAsync("Check your email");

                string resetLink;
                using (var scope = factory.CreateRealScope())
                {
                    var link = scope.ServiceProvider.GetRequiredService<TestEmailSink>().LastPasswordResetLink;
                    link.Should().NotBeNullOrEmpty();
                    resetLink = link!;
                }

                await page.GotoAsync(resetLink);
                await page.Locator("[data-testid='reset-password-new']").FillAsync(NewPassword);
                await page.Locator("[data-testid='reset-password-confirm']").FillAsync(NewPassword);
                await page.Locator("[data-testid='reset-password-submit']").ClickAsync();

                await Expect(page.Locator("[data-testid='reset-password-confirmation-heading']")).ToBeVisibleAsync();

                // The old password no longer works...
                await LoginAsync(page, email, OriginalPassword);
                await Expect(page.Locator("[data-testid='login-error']")).ToBeVisibleAsync();

                // ...but the new one does.
                await LoginAsync(page, email, NewPassword);
                await page.Locator("html[data-app-ready='true']").WaitForAsync();
                await Expect(page).ToHaveURLAsync(new Regex("/$"));
                await SignOutAsync(page);

                // Reusing the very same link fails - ResetPasswordAsync rotated the security
                // stamp the token was bound to (see ResetPassword.razor's remarks).
                await page.GotoAsync(resetLink);
                await page.Locator("[data-testid='reset-password-new']").FillAsync("Another-Str0ng-Pass1!");
                await page.Locator("[data-testid='reset-password-confirm']").FillAsync("Another-Str0ng-Pass1!");
                await page.Locator("[data-testid='reset-password-submit']").ClickAsync();
                await Expect(page.Locator("[data-testid='reset-password-invalid']")).ToBeVisibleAsync();
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Weak_new_password_is_rejected_and_the_link_stays_usable()
    {
        var factory = CreateSmtpFactory();
        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            var page = await context.NewPageAsync();
            try
            {
                var email = $"weak-{Guid.NewGuid():N}@example.com";
                await RegisterAndConfirmAsync(page, factory, email, OriginalPassword);

                await SubmitForgotPasswordAsync(page, email);

                string resetLink;
                using (var scope = factory.CreateRealScope())
                {
                    resetLink = scope.ServiceProvider.GetRequiredService<TestEmailSink>().LastPasswordResetLink!;
                }

                await page.GotoAsync(resetLink);
                await page.Locator("[data-testid='reset-password-new']").FillAsync("short1");
                await page.Locator("[data-testid='reset-password-confirm']").FillAsync("short1");
                await page.Locator("[data-testid='reset-password-submit']").ClickAsync();

                await Expect(page.Locator("[data-testid='reset-password-error']")).ToContainTextAsync("at least 12 characters");

                // The link is still valid - retry with a strong password on the same page.
                await page.Locator("[data-testid='reset-password-new']").FillAsync(NewPassword);
                await page.Locator("[data-testid='reset-password-confirm']").FillAsync(NewPassword);
                await page.Locator("[data-testid='reset-password-submit']").ClickAsync();

                await Expect(page.Locator("[data-testid='reset-password-confirmation-heading']")).ToBeVisibleAsync();
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Expired_reset_link_shows_a_generic_error_with_a_link_to_request_a_new_one()
    {
        var factory = CreateSmtpFactory(new Dictionary<string, string?>
        {
            ["Account__PasswordReset__TokenLifespan"] = "00:00:01",
        });
        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            var page = await context.NewPageAsync();
            try
            {
                var email = $"expired-{Guid.NewGuid():N}@example.com";
                await RegisterAndConfirmAsync(page, factory, email, OriginalPassword);

                await SubmitForgotPasswordAsync(page, email);

                string resetLink;
                using (var scope = factory.CreateRealScope())
                {
                    resetLink = scope.ServiceProvider.GetRequiredService<TestEmailSink>().LastPasswordResetLink!;
                }

                // Well past the 1-second lifespan configured above. The token is only actually
                // verified on submit (see ResetPassword.razor's remarks - visiting the link alone
                // only checks that userId/code are present), so submit the form once to trigger
                // that check.
                await Task.Delay(TimeSpan.FromSeconds(5));

                await page.GotoAsync(resetLink);
                await page.Locator("[data-testid='reset-password-new']").FillAsync(NewPassword);
                await page.Locator("[data-testid='reset-password-confirm']").FillAsync(NewPassword);
                await page.Locator("[data-testid='reset-password-submit']").ClickAsync();

                await Expect(page.Locator("[data-testid='reset-password-invalid']")).ToBeVisibleAsync();
                await Expect(page.Locator("[data-testid='reset-password-request-new']")).ToBeVisibleAsync();
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Missing_link_parameters_show_the_same_invalid_link_message()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/Account/ResetPassword");

        await Expect(page.Locator("[data-testid='reset-password-invalid']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='reset-password-request-new']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    private static async Task SubmitForgotPasswordAsync(IPage page, string email)
    {
        await page.GotoAsync("/Account/ForgotPassword");
        await page.Locator("[data-testid='forgot-password-email']").FillAsync(email);
        await page.Locator("[data-testid='forgot-password-submit']").ClickAsync();
    }

    private static async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("[data-testid='login-email']").FillAsync(email);
        await page.Locator("[data-testid='login-password']").FillAsync(password);
        await page.Locator("[data-testid='login-submit']").ClickAsync();
    }

    private static async Task SignOutAsync(IPage page)
    {
        await page.GotoAsync("/");
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await page.Locator("[data-testid='sign-out']").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login$"));
    }

    private static async Task RegisterAndConfirmAsync(IPage page, RelioWebAppFactory factory, string email, string password)
    {
        await page.GotoAsync("/Account/Register");
        await page.Locator("[data-testid='register-email']").FillAsync(email);
        await page.Locator("[data-testid='register-password']").FillAsync(password);
        await page.Locator("[data-testid='register-confirm-password']").FillAsync(password);
        await page.Locator("[data-testid='register-submit']").ClickAsync();
        await page.Locator("[data-testid='register-confirmation-heading']").WaitForAsync();

        string confirmationLink;
        using (var scope = factory.CreateRealScope())
        {
            confirmationLink = scope.ServiceProvider.GetRequiredService<TestEmailSink>().LastConfirmationLink!;
        }

        await page.GotoAsync(confirmationLink);
        await page.Locator("[data-testid='confirm-email-heading']").WaitForAsync();
    }

    /// <summary>
    /// Builds a variant app with <c>Email:Provider=Smtp</c> (so registration requires
    /// confirmation and password reset actually generates a link) and a <see cref="TestEmailSink"/>
    /// in place of a real SMTP sender - the same approach <see cref="EmailConfirmationTests"/>
    /// uses, with <see cref="RelioAppFixture"/>'s own remarks on why a fresh factory (not
    /// <c>WithWebHostBuilder</c>) is required. <paramref name="extraEnvironment"/> lets a test
    /// (e.g. the expired-link one) also override <c>Account:PasswordReset:*</c> for just this
    /// instance.
    /// </summary>
    private static RelioWebAppFactory CreateSmtpFactory(
        IReadOnlyDictionary<string, string?>? extraEnvironment = null)
    {
        var environment = new Dictionary<string, string?>
        {
            ["Email__Provider"] = "Smtp",
            ["Email__Smtp__FromAddress"] = "relio@example.com",
        };
        if (extraEnvironment is not null)
        {
            foreach (var (key, value) in extraEnvironment)
            {
                environment[key] = value;
            }
        }

        // See EmailConfirmationTests' matching comment: these must be environment variables set
        // before this factory's own builder.Build() call runs, and are safe to mutate only
        // because every test class in RelioAppCollection is serialized against the others.
        foreach (var (key, value) in environment)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            var factory = new RelioWebAppFactory(configureTestServices: services =>
            {
                services.AddSingleton<TestEmailSink>();
                services.AddScoped<IEmailSender<RelioUser>>(sp => sp.GetRequiredService<TestEmailSink>());
            });

            // Forces CreateHost to actually run while the environment variables above are set.
            _ = factory.Services;
            return factory;
        }
        finally
        {
            foreach (var key in environment.Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }
}
