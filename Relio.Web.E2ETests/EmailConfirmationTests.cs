using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;
using Relio.Data.Identity;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers the <c>Email:Provider=Smtp</c> branch (issue #15): confirmation becomes required before
/// sign-in, and a real email would be sent - here captured by <see cref="TestEmailSink"/> instead.
/// Builds its own app instance (see <see cref="RelioAppFixture"/>'s remarks on variant factories),
/// with <c>Email:Provider=Smtp</c> set only long enough to start that instance - see the comments
/// below for why this is safe despite environment variables being process-wide.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class EmailConfirmationTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Registering_with_Smtp_configured_requires_following_the_confirmation_link()
    {
        RelioWebAppFactory smtpFactory;

        // Program.cs reads Email:Provider (via Relio.Web.Identity.ServiceCollectionExtensions) in
        // a top-level statement before its own builder.Build() call, the same timing constraint
        // RelioWebAppFactory.CreateHost's own comments describe for Database:Provider - so this
        // must be an environment variable, set before that Build() call runs. It is reset
        // immediately after, and is safe to mutate here because every test class in this project
        // shares RelioAppCollection, so xUnit never runs another one of this assembly's tests
        // concurrently with this one.
        Environment.SetEnvironmentVariable("Email__Provider", "Smtp");
        Environment.SetEnvironmentVariable("Email__Smtp__FromAddress", "relio@example.com");
        try
        {
            smtpFactory = new RelioWebAppFactory(configureTestServices: services =>
            {
                services.AddSingleton<TestEmailSink>();
                services.AddScoped<IEmailSender<RelioUser>>(sp => sp.GetRequiredService<TestEmailSink>());
            });

            // Forces CreateHost to actually run while the environment variables above are set.
            _ = smtpFactory.Services;
        }
        finally
        {
            Environment.SetEnvironmentVariable("Email__Provider", null);
            Environment.SetEnvironmentVariable("Email__Smtp__FromAddress", null);
        }

        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { BaseURL = smtpFactory.ServerAddress });
            var page = await context.NewPageAsync();
            try
            {
                var email = $"smtp-{Guid.NewGuid():N}@example.com";
                const string password = "Str0ng-Passw0rd!";

                await page.GotoAsync("/Account/Register");
                await page.Locator("[data-testid='register-email']").FillAsync(email);
                await page.Locator("[data-testid='register-password']").FillAsync(password);
                await page.Locator("[data-testid='register-confirm-password']").FillAsync(password);
                await page.Locator("[data-testid='register-submit']").ClickAsync();

                await Expect(page.Locator("[data-testid='register-confirmation-heading']"))
                    .ToHaveTextAsync("Check your email");

                // Not signed in yet - confirming is required first.
                await page.GotoAsync("/dashboard");
                await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

                using (var scope = smtpFactory.CreateRealScope())
                {
                    var sink = scope.ServiceProvider.GetRequiredService<TestEmailSink>();
                    sink.LastConfirmationLink.Should().NotBeNullOrEmpty();

                    await page.GotoAsync(sink.LastConfirmationLink!);
                }

                await Expect(page.Locator("[data-testid='confirm-email-heading']")).ToHaveTextAsync("Email confirmed");

                await page.GotoAsync("/Account/Login");
                await page.Locator("[data-testid='login-email']").FillAsync(email);
                await page.Locator("[data-testid='login-password']").FillAsync(password);
                await page.Locator("[data-testid='login-submit']").ClickAsync();
                await page.Locator("html[data-app-ready='true']").WaitForAsync();

                await Expect(page).ToHaveURLAsync(new Regex("/dashboard$"));
                await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        finally
        {
            await smtpFactory.DisposeAsync();
        }
    }
}
