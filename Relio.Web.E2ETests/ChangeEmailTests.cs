using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #18's change-email flow end to end. With the default <c>Email:Provider=None</c>
/// (the shared fixture's app) a new address applies immediately, since no confirmation can be
/// sent; with <c>Email:Provider=Smtp</c> (a variant app with a <see cref="TestEmailSink"/>, the
/// same pattern <see cref="PasswordResetTests"/> uses) the address only changes once the link sent
/// to the new address is followed. Every test uses fresh users (see
/// <see cref="AccountTestHelpers"/>'s remarks).
/// </summary>
[Collection(RelioAppCollection.Name)]
public class ChangeEmailTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Without_an_email_provider_the_email_changes_immediately()
    {
        var oldEmail = NewEmail("emailold");
        var newEmail = NewEmail("emailnew");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, oldEmail, StrongPassword);

        await page.GotoAsync("/Account/Manage/Email");
        await Expect(page.Locator("[data-testid='manage-email-no-confirmation-note']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='manage-email-current']")).ToHaveTextAsync(oldEmail);

        await SubmitChangeEmailAsync(page, newEmail, StrongPassword);

        await Expect(page.Locator("[data-testid='manage-email-status']")).ToHaveTextAsync("Your email address has been changed.");
        await Expect(page.Locator("[data-testid='manage-email-current']")).ToHaveTextAsync(newEmail);

        // The session that changed it stays signed in, now as the new address.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(newEmail);

        // Sign-in follows the new address; the old one no longer works.
        await SignOutAsync(page);
        await LoginAsync(page, oldEmail, StrongPassword);
        await Expect(page.Locator("[data-testid='login-error']")).ToBeVisibleAsync();
        await LoginAndWaitForAppAsync(page, newEmail, StrongPassword);
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(newEmail);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Without_an_email_provider_a_duplicate_email_is_rejected()
    {
        var takenEmail = NewEmail("emailtaken");
        var takenPage = await fixture.NewPageAsync();
        await RegisterAsync(takenPage, takenEmail, StrongPassword);
        await RelioAppFixture.ClosePageAsync(takenPage);

        var myEmail = NewEmail("emailmine");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, myEmail, StrongPassword);

        await SubmitChangeEmailAsync(page, takenEmail, StrongPassword);

        await Expect(page.Locator("[data-testid='manage-email-error']"))
            .ToHaveTextAsync("That email address can't be used. Try a different one.");
        (await GetUserAsync(fixture.App, myEmail)).Should().NotBeNull("the address was not changed");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected()
    {
        var email = NewEmail("emailwrongpw");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, email, StrongPassword);

        await SubmitChangeEmailAsync(page, NewEmail("emailnew"), "Not-My-Passw0rd!!");

        await Expect(page.Locator("[data-testid='manage-email-error']")).ToHaveTextAsync("Your current password is incorrect.");
        (await GetUserAsync(fixture.App, email)).Should().NotBeNull("the address was not changed");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Changing_to_the_current_email_is_rejected()
    {
        var email = NewEmail("emailsame");
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, email, StrongPassword);

        await SubmitChangeEmailAsync(page, email.ToUpperInvariant(), StrongPassword);

        await Expect(page.Locator("[data-testid='manage-email-error']")).ToHaveTextAsync("That's already your email address.");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Missing_link_parameters_show_the_invalid_message()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/Account/ConfirmEmailChange");

        await Expect(page.Locator("[data-testid='confirm-email-change-heading']")).ToHaveTextAsync("Email not changed");
        await Expect(page.Locator("[data-testid='confirm-email-change-message']"))
            .ToHaveTextAsync("This email change link is invalid or has expired.");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task With_email_configured_changing_email_requires_following_the_link_sent_to_the_new_address()
    {
        var factory = CreateSmtpFactory();
        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            var page = await context.NewPageAsync();
            try
            {
                var oldEmail = NewEmail("smtpold");
                var newEmail = NewEmail("smtpnew");
                await RegisterAndConfirmAsync(page, factory, oldEmail, StrongPassword);
                await LoginAndWaitForAppAsync(page, oldEmail, StrongPassword);

                await page.GotoAsync("/Account/Manage/Email");
                await Expect(page.Locator("[data-testid='manage-email-no-confirmation-note']")).ToHaveCountAsync(0);

                await SubmitChangeEmailAsync(page, newEmail, StrongPassword);

                await Expect(page.Locator("[data-testid='manage-email-status']"))
                    .ToContainTextAsync("we've sent a confirmation link");

                // The link went to the NEW address and carries no email address at all.
                string link;
                using (var scope = factory.CreateRealScope())
                {
                    var sink = scope.ServiceProvider.GetRequiredService<TestEmailSink>();
                    sink.LastConfirmationRecipient.Should().Be(newEmail);
                    link = sink.LastConfirmationLink!;
                }

                link.Should().Contain("/Account/ConfirmEmailChange");
                link.Should().NotContainEquivalentOf(newEmail);
                link.Should().NotContainEquivalentOf(Uri.EscapeDataString(newEmail));
                link.Should().NotContainEquivalentOf(oldEmail);
                link.Should().NotContainEquivalentOf(Uri.EscapeDataString(oldEmail));

                // Nothing changed until the link is followed.
                (await GetUserAsync(factory, oldEmail)).Should().NotBeNull();
                (await GetUserAsync(factory, newEmail)).Should().BeNull();

                await page.GotoAsync(link);
                await Expect(page.Locator("[data-testid='confirm-email-change-heading']")).ToHaveTextAsync("Email address changed");
                await Expect(page.Locator("[data-testid='confirm-email-change-continue']")).ToHaveAttributeAsync("href", "/settings");

                (await GetUserAsync(factory, oldEmail)).Should().BeNull();
                var changed = await GetUserAsync(factory, newEmail);
                changed.Should().NotBeNull();
                changed!.PendingEmail.Should().BeNull();

                // The session that followed the link is now signed in as the new address.
                await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");
                await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(newEmail);

                await SignOutAsync(page);
                await LoginAsync(page, oldEmail, StrongPassword);
                await Expect(page.Locator("[data-testid='login-error']")).ToBeVisibleAsync();
                await LoginAndWaitForAppAsync(page, newEmail, StrongPassword);
                await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(newEmail);

                // The link is single use.
                await page.GotoAsync(link);
                await Expect(page.Locator("[data-testid='confirm-email-change-heading']")).ToHaveTextAsync("Email not changed");
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
    public async Task With_email_configured_a_newer_request_invalidates_the_older_link()
    {
        var factory = CreateSmtpFactory();
        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            var page = await context.NewPageAsync();
            try
            {
                var oldEmail = NewEmail("supersedeold");
                var firstEmail = NewEmail("supersedefirst");
                var secondEmail = NewEmail("supersedesecond");
                await RegisterAndConfirmAsync(page, factory, oldEmail, StrongPassword);
                await LoginAndWaitForAppAsync(page, oldEmail, StrongPassword);

                await SubmitChangeEmailAsync(page, firstEmail, StrongPassword);
                await Expect(page.Locator("[data-testid='manage-email-status']")).ToBeVisibleAsync();
                var firstLink = LastConfirmationLink(factory);

                await SubmitChangeEmailAsync(page, secondEmail, StrongPassword);
                await Expect(page.Locator("[data-testid='manage-email-status']")).ToBeVisibleAsync();
                var secondLink = LastConfirmationLink(factory);
                secondLink.Should().NotBe(firstLink);

                await page.GotoAsync(firstLink);
                await Expect(page.Locator("[data-testid='confirm-email-change-heading']")).ToHaveTextAsync("Email not changed");
                (await GetUserAsync(factory, firstEmail)).Should().BeNull();

                await page.GotoAsync(secondLink);
                await Expect(page.Locator("[data-testid='confirm-email-change-heading']")).ToHaveTextAsync("Email address changed");
                (await GetUserAsync(factory, secondEmail)).Should().NotBeNull();
                (await GetUserAsync(factory, firstEmail)).Should().BeNull();
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
    public async Task With_email_configured_a_duplicate_email_shows_the_same_message_and_sends_nothing()
    {
        var factory = CreateSmtpFactory();
        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { BaseURL = factory.ServerAddress });
            var page = await context.NewPageAsync();
            try
            {
                var takenEmail = NewEmail("smtptaken");
                var myEmail = NewEmail("smtpmine");
                await RegisterAndConfirmAsync(page, factory, takenEmail, StrongPassword);
                await RegisterAndConfirmAsync(page, factory, myEmail, StrongPassword);
                await LoginAndWaitForAppAsync(page, myEmail, StrongPassword);

                int emailsBefore;
                using (var scope = factory.CreateRealScope())
                {
                    emailsBefore = scope.ServiceProvider.GetRequiredService<TestEmailSink>().ConfirmationEmailCount;
                }

                await SubmitChangeEmailAsync(page, takenEmail, StrongPassword);

                // Exactly what a usable address shows - nothing reveals that this one is registered.
                await Expect(page.Locator("[data-testid='manage-email-status']"))
                    .ToContainTextAsync("we've sent a confirmation link");

                using (var scope = factory.CreateRealScope())
                {
                    scope.ServiceProvider.GetRequiredService<TestEmailSink>().ConfirmationEmailCount.Should().Be(emailsBefore);
                }

                (await GetUserAsync(factory, myEmail))!.PendingEmail.Should().BeNull();
                (await GetUserAsync(factory, takenEmail)).Should().NotBeNull();
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

    private static string LastConfirmationLink(RelioWebAppFactory factory)
    {
        using var scope = factory.CreateRealScope();
        return scope.ServiceProvider.GetRequiredService<TestEmailSink>().LastConfirmationLink!;
    }

    private static async Task SubmitChangeEmailAsync(IPage page, string newEmail, string password)
    {
        await page.GotoAsync("/Account/Manage/Email");
        await page.Locator("[data-testid='manage-email-new']").FillAsync(newEmail);
        await page.Locator("[data-testid='manage-email-password']").FillAsync(password);
        await page.Locator("[data-testid='manage-email-submit']").ClickAsync();
    }
}
