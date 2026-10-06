using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data.Identity;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// Helpers shared by the account-related E2E tests (<see cref="PasswordResetTests"/>,
/// <see cref="AccountSettingsTests"/>, <see cref="ChangePasswordTests"/>,
/// <see cref="ChangeEmailTests"/>): registering and signing in real users through the real pages,
/// and building variant apps.
/// </summary>
/// <remarks>
/// Tests that change account state (password, email, time zone) always register a fresh
/// <c>...@example.com</c> user instead of reusing the seeded demo user: the demo user is shared by
/// the whole collection, and changing its credentials would break every test that signs in as it.
/// </remarks>
public static class AccountTestHelpers
{
    /// <summary>A password that satisfies Relio's password policy.</summary>
    public const string StrongPassword = "Str0ng-Passw0rd!";

    /// <summary>A second password that satisfies Relio's password policy.</summary>
    public const string OtherStrongPassword = "Even-Str0nger-Pass1!";

    /// <summary>A unique, never-reused email address.</summary>
    public static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// Fills in and submits the real Register page and waits for its confirmation page. With
    /// <c>Email:Provider=None</c> (the shared fixture's default) this also signs the new user in.
    /// </summary>
    public static async Task RegisterAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Register");
        await page.Locator("[data-testid='register-email']").FillAsync(email);
        await page.Locator("[data-testid='register-password']").FillAsync(password);
        await page.Locator("[data-testid='register-confirm-password']").FillAsync(password);
        await page.Locator("[data-testid='register-submit']").ClickAsync();
        await page.Locator("[data-testid='register-confirmation-heading']").WaitForAsync();
    }

    /// <summary>
    /// Registers a new user in the app <paramref name="page"/> is browsing and, with
    /// <c>Email:Provider=Smtp</c>, follows the captured confirmation link so the account is
    /// usable. The user is not signed in afterwards (use <see cref="LoginAsync"/>).
    /// </summary>
    public static async Task RegisterAndConfirmAsync(IPage page, RelioWebAppFactory factory, string email, string password)
    {
        await RegisterAsync(page, email, password);

        string confirmationLink;
        using (var scope = factory.CreateRealScope())
        {
            confirmationLink = scope.ServiceProvider.GetRequiredService<TestEmailSink>().LastConfirmationLink!;
        }

        await page.GotoAsync(confirmationLink);
        await page.Locator("[data-testid='confirm-email-heading']").WaitForAsync();
    }

    /// <summary>Submits the real Login page (does not wait for the app to load).</summary>
    public static async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("[data-testid='login-email']").FillAsync(email);
        await page.Locator("[data-testid='login-password']").FillAsync(password);
        await page.Locator("[data-testid='login-submit']").ClickAsync();
    }

    /// <summary>Signs in through the real Login page and waits for the app's first interactive render.</summary>
    public static async Task LoginAndWaitForAppAsync(IPage page, string email, string password)
    {
        await LoginAsync(page, email, password);
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
    }

    /// <summary>Signs the current user out through the app bar's real Sign out button.</summary>
    public static async Task SignOutAsync(IPage page)
    {
        await page.GotoAsync("/");
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
        await page.Locator("[data-testid='sign-out']").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login$"));
    }

    /// <summary>Looks a user up by email in the running app's database, or null if there is none.</summary>
    public static async Task<RelioUser?> GetUserAsync(RelioWebAppFactory factory, string email)
    {
        using var scope = factory.CreateRealScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>().FindByEmailAsync(email);
    }

    /// <summary>
    /// Builds a variant app with <c>Email:Provider=Smtp</c> (so registration requires
    /// confirmation and password reset / email change actually send a link) and a
    /// <see cref="TestEmailSink"/> in place of a real SMTP sender - the same approach
    /// <see cref="EmailConfirmationTests"/> uses, with <see cref="RelioAppFixture"/>'s own remarks
    /// on why a fresh factory (not <c>WithWebHostBuilder</c>) is required.
    /// </summary>
    /// <param name="extraEnvironment">
    /// Lets a test also override other configuration (e.g. <c>Account__PasswordReset__TokenLifespan</c>)
    /// for just this instance.
    /// </param>
    public static RelioWebAppFactory CreateSmtpFactory(IReadOnlyDictionary<string, string?>? extraEnvironment = null)
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

    /// <summary>
    /// Builds a variant app with the default <c>Email:Provider=None</c> and
    /// <paramref name="configureServices"/> applied (e.g. a zero security stamp validation
    /// interval, to prove another session is signed out).
    /// </summary>
    public static RelioWebAppFactory CreateFactory(Action<IServiceCollection> configureServices)
    {
        var factory = new RelioWebAppFactory(configureServices);

        // Forces CreateHost to actually run.
        _ = factory.Services;
        return factory;
    }
}
