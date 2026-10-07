using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Data.People;
using Relio.Data.Seeding;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #19's administration page end to end. The shared fixture app's demo account is its
/// Administrator (see <see cref="DemoDataSeeder"/>); every account these tests act on is a fresh
/// registered user, never the demo account (which every other test signs in as).
/// </summary>
[Collection(RelioAppCollection.Name)]
public class AdministrationTests(RelioAppFixture fixture)
{
    private static async Task SignInAsAdministratorAsync(IPage page)
    {
        await LoginAndWaitForAppAsync(page, DemoDataSeeder.DemoEmail, DemoDataSeeder.DemoPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/admin/users");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Administration", Exact = true })).ToBeVisibleAsync();
    }

    private static ILocator RowFor(IPage page, string email) =>
        page.Locator("tr", new() { HasText = email });

    private static async Task ConfirmDisableAsync(IPage page) =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Disable account", Exact = true }).ClickAsync();

    [Fact]
    public async Task Administrator_sees_accounts_and_the_administration_link()
    {
        var email = NewEmail("listed");
        var registration = await fixture.NewPageAsync();
        await RegisterAsync(registration, email, StrongPassword);
        await RelioAppFixture.ClosePageAsync(registration);

        var page = await fixture.NewPageAsync();
        await RelioAppFixture.SignInAsDemoAsync(page);
        await Expect(page.Locator("nav[aria-label='Primary']").GetByText("Administration", new() { Exact = true }))
            .ToBeVisibleAsync();
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/admin/users");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Administration", Exact = true })).ToBeVisibleAsync();
        await Expect(RowFor(page, DemoDataSeeder.DemoEmail).Locator("[data-testid='admin-account-role-administrator']")).ToBeVisibleAsync();
        await Expect(RowFor(page, email)).ToBeVisibleAsync();
        await Expect(RowFor(page, email).Locator("[data-testid='admin-account-role-administrator']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='admin-registration-mode']")).ToContainTextAsync("open");

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Non_administrator_is_denied_the_administration_page()
    {
        var page = await fixture.NewPageAsync();
        await RegisterAsync(page, NewEmail("plain"), StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");
        await Expect(page.Locator("nav[aria-label='Primary']").GetByText("Administration", new() { Exact = true }))
            .ToHaveCountAsync(0);

        // A full request: the authorization middleware says no.
        await page.GotoAsync("/admin/users");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/AccessDenied"));

        // Navigating inside the open circuit skips the HTTP pipeline; the router must say no too,
        // and say "access denied" rather than bouncing a signed-in person to the login page.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");
        await page.EvaluateAsync("Blazor.navigateTo('/admin/users')");
        await Expect(page).ToHaveURLAsync(new Regex("/Account/AccessDenied"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access denied" })).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Anonymous_visitor_is_sent_to_the_login_page()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/admin/users");

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Administrator_cannot_disable_their_own_account()
    {
        var page = await fixture.NewPageAsync();
        await SignInAsAdministratorAsync(page);

        var ownRow = RowFor(page, DemoDataSeeder.DemoEmail);
        await Expect(ownRow).ToBeVisibleAsync();
        await Expect(ownRow.Locator("[data-testid='admin-account-disable']")).ToHaveCountAsync(0);
        await Expect(ownRow.Locator("[data-testid='admin-account-enable']")).ToHaveCountAsync(0);

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Administrator_can_disable_and_re_enable_an_account()
    {
        var email = NewEmail("toggle");
        var registration = await fixture.NewPageAsync();
        await RegisterAsync(registration, email, StrongPassword);
        await RelioAppFixture.ClosePageAsync(registration);

        var admin = await fixture.NewPageAsync();
        await SignInAsAdministratorAsync(admin);
        var row = RowFor(admin, email);

        await row.Locator("[data-testid='admin-account-disable']").ClickAsync();
        await ConfirmDisableAsync(admin);
        await Expect(row.Locator("[data-testid='admin-account-status-disabled']")).ToBeVisibleAsync();

        // Disabled: the right password is refused with an honest message, a wrong one is not
        // distinguishable from any other wrong password.
        var user = await fixture.NewPageAsync();
        await LoginAsync(user, email, StrongPassword);
        await Expect(user.Locator("[data-testid='login-error']"))
            .ToContainTextAsync("This account has been disabled");
        await LoginAsync(user, email, "Wr0ng-Passw0rd!!");
        await Expect(user.Locator("[data-testid='login-error']")).ToHaveTextAsync("Email or password is incorrect.");

        // Enabled again: signing in works, as if nothing happened.
        await row.Locator("[data-testid='admin-account-enable']").ClickAsync();
        await Expect(row.Locator("[data-testid='admin-account-status-disabled']")).ToHaveCountAsync(0);
        await LoginAndWaitForAppAsync(user, email, StrongPassword);
        await Expect(user.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

        await RelioAppFixture.ClosePageAsync(admin);
        await RelioAppFixture.ClosePageAsync(user);
    }

    [Fact]
    public async Task Administrator_never_sees_other_users_people()
    {
        var email = NewEmail("private");
        var personName = $"Zephyrine{Guid.NewGuid():N}"[..20];
        var registration = await fixture.NewPageAsync();
        await RegisterAsync(registration, email, StrongPassword);
        await RelioAppFixture.ClosePageAsync(registration);

        using (var scope = fixture.App.CreateRealScope())
        {
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>().FindByEmailAsync(email))!;
            var people = new PeopleService(
                scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
                new FixedCurrentUser(user.Id),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            await people.CreateAsync(new CreatePersonRequest { FirstName = personName, LastName = "Test" });
        }

        var admin = await fixture.NewPageAsync();
        await SignInAsAdministratorAsync(admin);
        await Expect(RowFor(admin, email)).ToBeVisibleAsync();

        (await admin.ContentAsync()).Should().NotContain(personName);
        await Expect(RowFor(admin, email).Locator("a")).ToHaveCountAsync(0);

        // Nor does the administrator's own people list contain it.
        // The list loads after the circuit connects, so wait for it (the count line) before asserting
        // that a name is absent; archived people are included so nothing hides in the other view.
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(admin, "/people?archived=true");
        await Expect(admin.Locator("[data-testid='people-count']")).ToBeVisibleAsync();
        (await admin.ContentAsync()).Should().NotContain(personName);

        await RelioAppFixture.ClosePageAsync(admin);
    }

    [Fact]
    public async Task Disabling_an_account_ends_its_existing_session()
    {
        // A one-second validation interval: the open circuit re-checks the account almost at once
        // instead of after the default 30 minutes.
        await using var app = VariantApp.Create(
            fixture, new Dictionary<string, string?> { ["Account__Session__ValidationInterval"] = "00:00:01" });
        var email = NewEmail("session");

        var userPage = await app.NewPageAsync();
        await RegisterAsync(userPage, email, StrongPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(userPage, "/dashboard");
        await Expect(userPage.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(email);

        var adminPage = await app.NewPageAsync();
        await SignInAsAdministratorAsync(adminPage);
        var row = RowFor(adminPage, email);
        await row.Locator("[data-testid='admin-account-disable']").ClickAsync();
        await ConfirmDisableAsync(adminPage);
        await Expect(row.Locator("[data-testid='admin-account-status-disabled']")).ToBeVisibleAsync();

        // The already-open circuit notices at its next revalidation: it is no longer signed in
        // (the app bar's "signed in as" link is gone)...
        await Expect(userPage.Locator("[data-testid='signed-in-as']")).ToHaveCountAsync(0, new() { Timeout = 20_000 });

        // ...and the cookie no longer works for a fresh request either.
        await userPage.GotoAsync("/dashboard");
        await Expect(userPage).ToHaveURLAsync(new Regex("/Account/Login"));
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }
}
