using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data.Identity;
using Relio.Web.Email;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Exercises #59 through real static-SSR account pages and two independent browser sessions.
/// This suite uses a fresh account and never changes the shared demo Administrator.
/// </summary>
[Collection(RelioAppCollection.Name)]
public sealed class AccountDeletionTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Email_delivery_failure_does_not_undo_or_misreport_committed_erasure()
    {
        var factory = CreateSmtpFactoryWithDeletionSender();
        IPage? page = null;
        try
        {
            page = await NewPageAsync(factory);
            var email = NewEmail("erase-mail-failure");
            await RegisterAndConfirmAsync(page, factory, email, StrongPassword);
            await LoginAndWaitForAppAsync(page, email, StrongPassword);

            using (var scope = factory.CreateRealScope())
            {
                scope.ServiceProvider.GetRequiredService<TestAccountDeletionConfirmationSender>().FailDelivery = true;
            }

            await page.GotoAsync("/Account/Manage/DeleteAccount");
            await page.Locator("[data-testid='delete-account-password']").FillAsync(StrongPassword);
            await page.Locator("[data-testid='delete-account-confirm']").CheckAsync();
            await page.Locator("[data-testid='delete-account-submit']").ClickAsync();

            await Expect(page).ToHaveURLAsync(new Regex("/Account/AccountDeleted\\?delivery=failed$"));
            await Expect(page.Locator("[data-testid='account-deleted-message']"))
                .ToContainTextAsync("permanently deleted from the live database");
            await Expect(page.Locator("[data-testid='account-deleted-email']"))
                .ToContainTextAsync("account was deleted, but the confirmation email could not be delivered");

            using var verify = factory.CreateRealScope();
            (await verify.ServiceProvider.GetRequiredService<UserManager<RelioUser>>()
                .FindByEmailAsync(email)).Should().BeNull();
        }
        finally
        {
            if (page is not null)
            {
                await RelioAppFixture.ClosePageAsync(page);
            }

            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Erasure_requires_a_current_password_offers_export_sends_a_notice_and_revokes_sessions()
    {
        var factory = CreateSmtpFactoryWithDeletionSender();
        IPage? deletingPage = null;
        IPage? secondSessionPage = null;
        try
        {
            deletingPage = await NewPageAsync(factory);
            secondSessionPage = await NewPageAsync(factory);

            var email = NewEmail("erase");
            var survivingEmail = NewEmail("survives");
            await RegisterAndConfirmAsync(deletingPage, factory, survivingEmail, StrongPassword);
            await RegisterAndConfirmAsync(deletingPage, factory, email, StrongPassword);
            await LoginAndWaitForAppAsync(deletingPage, email, StrongPassword);
            await LoginAndWaitForAppAsync(secondSessionPage, email, StrongPassword);
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(secondSessionPage, "/settings");

            await deletingPage.GotoAsync("/Account/Manage/DeleteAccount");
            await Expect(deletingPage.GetByRole(AriaRole.Heading, new()
            {
                Name = "Delete your account",
                Exact = true,
            })).ToBeVisibleAsync();
            await Expect(deletingPage.Locator("[data-testid='delete-account-export-first']"))
                .ToHaveAttributeAsync("href", "/settings/data");

            await deletingPage.Locator("[data-testid='delete-account-password']").FillAsync("Wrong-Passw0rd!");
            await deletingPage.Locator("[data-testid='delete-account-confirm']").CheckAsync();
            await deletingPage.Locator("[data-testid='delete-account-submit']").ClickAsync();
            await Expect(deletingPage.Locator("[data-testid='delete-account-error']"))
                .ToHaveTextAsync("Your current password is incorrect.");
            (await GetUserAsync(factory, email)).Should().NotBeNull();

            await deletingPage.Locator("[data-testid='delete-account-password']").FillAsync(StrongPassword);
            await deletingPage.Locator("[data-testid='delete-account-confirm']").CheckAsync();
            await deletingPage.Locator("[data-testid='delete-account-submit']").ClickAsync();
            await Expect(deletingPage.Locator("[data-testid='account-deleted-heading']")).ToBeVisibleAsync();
            await Expect(deletingPage).ToHaveURLAsync(new Regex("/Account/AccountDeleted\\?delivery=sent$"));
            await Expect(deletingPage.Locator("[data-testid='account-deleted-email']"))
                .ToContainTextAsync("confirmation email was sent");

            using (var scope = factory.CreateRealScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<TestAccountDeletionConfirmationSender>();
                sender.Recipients.Should().Contain(email);
                var deletedUser = await scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>()
                    .FindByEmailAsync(email);
                deletedUser.Should().BeNull();
                var survivingUser = await scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>()
                    .FindByEmailAsync(survivingEmail);
                survivingUser.Should().NotBeNull();
            }

            await Expect(secondSessionPage.Locator("[data-testid='account-deleted-heading']")).ToBeVisibleAsync();
            await secondSessionPage.GotoAsync("/settings");
            await Expect(secondSessionPage).ToHaveURLAsync(new Regex("/Account/Login"));

            await LoginAndWaitForAppAsync(secondSessionPage, survivingEmail, StrongPassword);
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(secondSessionPage, "/settings");
            await Expect(secondSessionPage.Locator("[data-testid='settings-current-email']"))
                .ToHaveTextAsync(survivingEmail);
        }
        finally
        {
            if (deletingPage is not null)
            {
                await RelioAppFixture.ClosePageAsync(deletingPage);
            }

            if (secondSessionPage is not null)
            {
                await RelioAppFixture.ClosePageAsync(secondSessionPage);
            }

            await factory.DisposeAsync();
        }
    }

    private async Task<IPage> NewPageAsync(RelioWebAppFactory factory)
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = factory.ServerAddress,
            ViewportSize = Viewports.Desktop,
        });
        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });
        return await context.NewPageAsync();
    }

    private static RelioWebAppFactory CreateSmtpFactoryWithDeletionSender()
    {
        Environment.SetEnvironmentVariable("Email__Provider", "Smtp");
        Environment.SetEnvironmentVariable("Email__Smtp__FromAddress", "relio@example.com");
        try
        {
            var factory = new RelioWebAppFactory(configureTestServices: services =>
            {
                services.AddSingleton<TestEmailSink>();
                services.AddScoped<IEmailSender<RelioUser>>(sp => sp.GetRequiredService<TestEmailSink>());
                services.AddSingleton<TestAccountDeletionConfirmationSender>();
                services.AddScoped<IAccountDeletionConfirmationSender>(
                    sp => sp.GetRequiredService<TestAccountDeletionConfirmationSender>());
            });
            _ = factory.Services;
            return factory;
        }
        finally
        {
            Environment.SetEnvironmentVariable("Email__Provider", null);
            Environment.SetEnvironmentVariable("Email__Smtp__FromAddress", null);
        }
    }

    private sealed class TestAccountDeletionConfirmationSender : IAccountDeletionConfirmationSender
    {
        public ConcurrentQueue<string> Recipients { get; } = new();

        public bool FailDelivery { get; set; }

        public bool IsAvailable => true;

        public Task SendAsync(string address, CancellationToken cancellationToken = default)
        {
            Recipients.Enqueue(address);
            if (FailDelivery)
            {
                throw new InvalidOperationException("Synthetic email delivery failure.");
            }

            return Task.CompletedTask;
        }
    }
}
