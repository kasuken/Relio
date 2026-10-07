using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Application.Administration;
using Relio.Data.Identity;
using Relio.Data.Seeding;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>
/// Covers issue #19's sign-up control end to end: the first account becomes the administrator,
/// <c>Registration:Mode</c> closes or restricts sign-up, and an invitation is a single-use link
/// bound to one email address. Most tests run a variant app (<see cref="VariantApp"/>) because they
/// need a particular mode or an instance with no accounts yet.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class RegistrationModeTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task First_account_on_a_fresh_instance_becomes_the_administrator()
    {
        await using var app = VariantApp.Create(fixture, seedDemoData: false);

        // The first account.
        var first = await app.NewPageAsync();
        var firstEmail = NewEmail("first");
        await first.GotoAsync("/Account/Register");
        await Expect(first.Locator("[data-testid='register-first-account-note']")).ToBeVisibleAsync();
        await RegisterAsync(first, firstEmail, StrongPassword);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(first, "/");
        await Expect(first.Locator("nav[aria-label='Primary']").GetByText("Administration", new() { Exact = true }))
            .ToBeVisibleAsync();
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(first, "/admin/users");
        await Expect(first.GetByRole(AriaRole.Heading, new() { Name = "Administration", Exact = true })).ToBeVisibleAsync();

        // The second account is an ordinary user: no link, and the page is denied.
        var second = await app.NewPageAsync();
        var secondEmail = NewEmail("second");
        await second.GotoAsync("/Account/Register");
        await Expect(second.Locator("[data-testid='register-first-account-note']")).ToHaveCountAsync(0);
        await RegisterAsync(second, secondEmail, StrongPassword);

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(second, "/");
        await Expect(second.Locator("nav[aria-label='Primary']").GetByText("Administration", new() { Exact = true }))
            .ToHaveCountAsync(0);
        await second.GotoAsync("/admin/users");
        await Expect(second).ToHaveURLAsync(new Regex("/Account/AccessDenied"));

        (await IsAdministratorAsync(app.Factory, firstEmail)).Should().BeTrue();
        (await IsAdministratorAsync(app.Factory, secondEmail)).Should().BeFalse();
    }

    [Fact]
    public async Task Closed_mode_makes_the_sign_up_page_unavailable()
    {
        await using var app = VariantApp.Create(fixture, new Dictionary<string, string?> { ["Registration__Mode"] = "Closed" });
        var page = await app.NewPageAsync();

        var response = await page.GotoAsync("/Account/Register");

        response!.Status.Should().Be(403);
        await Expect(page.Locator("[data-testid='register-closed']")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign-up is closed" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='register-submit']")).ToHaveCountAsync(0);
        await Expect(page.Locator("input[name='Input.Password']")).ToHaveCountAsync(0);

        await page.GotoAsync("/Account/Login");
        await Expect(page.Locator("[data-testid='login-submit']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='login-create-account']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Closed_mode_still_lets_the_first_account_be_created()
    {
        await using var app = VariantApp.Create(
            fixture, new Dictionary<string, string?> { ["Registration__Mode"] = "Closed" }, seedDemoData: false);
        var page = await app.NewPageAsync();
        var email = NewEmail("closed-first");

        var response = await page.GotoAsync("/Account/Register");
        response!.Status.Should().Be(200);
        await Expect(page.Locator("[data-testid='register-first-account-note']")).ToBeVisibleAsync();
        await RegisterAsync(page, email, StrongPassword);

        (await IsAdministratorAsync(app.Factory, email)).Should().BeTrue();

        // Now there is an account, so the instance really is closed.
        var other = await app.NewPageAsync();
        var otherResponse = await other.GotoAsync("/Account/Register");
        otherResponse!.Status.Should().Be(403);
        await Expect(other.Locator("[data-testid='register-closed']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Invite_only_registration_requires_a_valid_single_use_invitation()
    {
        await using var app = VariantApp.Create(fixture, new Dictionary<string, string?> { ["Registration__Mode"] = "InviteOnly" });
        var invitedEmail = NewEmail("invited");

        // Without an invitation, or with a made-up one, there is no form.
        var anonymous = await app.NewPageAsync();
        await anonymous.GotoAsync("/Account/Register");
        await Expect(anonymous.Locator("[data-testid='register-invite-required']")).ToBeVisibleAsync();
        await Expect(anonymous.Locator("[data-testid='register-submit']")).ToHaveCountAsync(0);
        await anonymous.GotoAsync("/Account/Register?invite=bogus");
        await Expect(anonymous.Locator("[data-testid='register-invite-invalid']")).ToBeVisibleAsync();
        await anonymous.GotoAsync("/Account/Login");
        await Expect(anonymous.Locator("[data-testid='login-create-account']")).ToHaveCountAsync(0);

        // The administrator (the demo account) creates an invitation.
        var admin = await app.NewPageAsync();
        await LoginAndWaitForAppAsync(admin, DemoDataSeeder.DemoEmail, DemoDataSeeder.DemoPassword);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(admin, "/admin/users");
        await Expect(admin.Locator("[data-testid='admin-registration-mode']")).ToContainTextAsync("invitation");
        await admin.GetByLabel("Email address").FillAsync(invitedEmail);
        await admin.Locator("[data-testid='admin-invite-submit']").ClickAsync();
        var linkLocator = admin.Locator("[data-testid='admin-invite-link']");
        await Expect(linkLocator).ToBeVisibleAsync();
        var link = (await linkLocator.InnerTextAsync()).Trim();
        link.Should().Contain("/Account/Register?invite=");
        link.Should().NotContain(invitedEmail).And.NotContain("%40");

        // An invitation for one address does not work for another.
        var invitee = await app.NewPageAsync();
        await invitee.GotoAsync(link);
        await Expect(invitee.Locator("[data-testid='register-email']")).ToHaveValueAsync(invitedEmail);
        await invitee.Locator("[data-testid='register-email']").EvaluateAsync("e => { e.removeAttribute('readonly'); }");
        await invitee.Locator("[data-testid='register-email']").FillAsync(NewEmail("someone-else"));
        await invitee.Locator("[data-testid='register-password']").FillAsync(StrongPassword);
        await invitee.Locator("[data-testid='register-confirm-password']").FillAsync(StrongPassword);
        await invitee.Locator("[data-testid='register-submit']").ClickAsync();
        await Expect(invitee.Locator("[data-testid='register-error']"))
            .ToContainTextAsync("This invitation is for a different email address");

        // The invited address works, once.
        var inviteePage = await app.NewPageAsync();
        await inviteePage.GotoAsync(link);
        await inviteePage.Locator("[data-testid='register-password']").FillAsync(StrongPassword);
        await inviteePage.Locator("[data-testid='register-confirm-password']").FillAsync(StrongPassword);
        await inviteePage.Locator("[data-testid='register-submit']").ClickAsync();
        await inviteePage.Locator("[data-testid='onboarding-page']").WaitForAsync();
        (await IsAdministratorAsync(app.Factory, invitedEmail)).Should().BeFalse();

        var reuse = await app.NewPageAsync();
        await reuse.GotoAsync(link);
        await Expect(reuse.Locator("[data-testid='register-invite-invalid']")).ToBeVisibleAsync();
        await Expect(reuse.Locator("[data-testid='register-submit']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Open_mode_shows_the_create_account_link()
    {
        // The shared fixture app runs in the default Open mode.
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/Account/Login");

        await Expect(page.Locator("[data-testid='login-create-account']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    private static async Task<bool> IsAdministratorAsync(RelioWebAppFactory factory, string email)
    {
        using var scope = factory.CreateRealScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        return await userManager.IsInRoleAsync(user!, RelioRoles.Administrator);
    }
}
