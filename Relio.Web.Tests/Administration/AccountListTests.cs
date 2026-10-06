using Bunit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Administration;
using Relio.Web.Components.Administration;
using Relio.Web.Identity;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Administration;

public class AccountListTests
{
    private static BunitContext CreateContext(FakeUserAdministrationService service, bool requireConfirmedAccount = false)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddOptions();
        context.Services.Configure<AccountOptions>(_ => { });
        context.Services.Configure<IdentityOptions>(o => o.SignIn.RequireConfirmedAccount = requireConfirmedAccount);
        context.Services.AddSingleton<IUserAdministrationService>(service);
        context.Render<MudPopoverProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }

    private static FakeUserAdministrationService TwoAccounts()
    {
        var service = new FakeUserAdministrationService();
        service.Accounts.Add(new AccountSummary("a", "admin@example.com", true, true, false, false, IsCurrentUser: true));
        service.Accounts.Add(new AccountSummary("b", "member@example.com", true, false, false, false, IsCurrentUser: false));
        service.Accounts.Add(new AccountSummary("c", "off@example.com", true, false, true, false, IsCurrentUser: false));
        return service;
    }

    [Fact]
    public async Task Lists_every_account_with_its_status()
    {
        await using var context = CreateContext(TwoAccounts());

        var cut = context.Render<AccountList>();

        cut.FindAll("[data-testid='admin-account-email']").Select(e => e.TextContent.Trim())
            .Should().Equal("admin@example.com", "member@example.com", "off@example.com");
        cut.FindAll("[data-testid='admin-account-role-administrator']").Should().ContainSingle();
        cut.FindAll("[data-testid='admin-account-status-disabled']").Should().ContainSingle();
    }

    [Fact]
    public async Task Current_users_row_has_no_disable_button()
    {
        await using var context = CreateContext(TwoAccounts());

        var cut = context.Render<AccountList>();

        // Three rows, two of them with an action (the other account to disable, the disabled one to enable).
        cut.FindAll("[data-testid='admin-account-disable']").Should().ContainSingle();
        cut.FindAll("[data-testid='admin-account-enable']").Should().ContainSingle();
        var currentRow = cut.FindAll("tr").Single(r => r.TextContent.Contains("admin@example.com"));
        currentRow.QuerySelector("button").Should().BeNull("an administrator can't disable their own account");
    }

    [Fact]
    public async Task Rows_contain_no_links()
    {
        // No page for an individual account exists, and none must grow into one: an administrator
        // manages accounts, they do not browse anyone's notebook.
        await using var context = CreateContext(TwoAccounts());

        var cut = context.Render<AccountList>();

        cut.FindAll("tbody a").Should().BeEmpty();
        cut.FindAll("[href]").Should().BeEmpty();
    }

    [Fact]
    public async Task Enabling_a_disabled_account_calls_the_service_and_refreshes()
    {
        var service = TwoAccounts();
        await using var context = CreateContext(service);
        var cut = context.Render<AccountList>();

        cut.Find("[data-testid='admin-account-enable']").Click();

        cut.WaitForAssertion(() => service.Enabled.Should().Equal("c"));
    }

    [Fact]
    public async Task Unconfirmed_is_only_shown_when_confirmation_is_required()
    {
        var service = new FakeUserAdministrationService();
        service.Accounts.Add(new AccountSummary("b", "member@example.com", false, false, false, false, false));

        await using (var withoutConfirmation = CreateContext(service))
        {
            withoutConfirmation.Render<AccountList>().Markup.Should().NotContain("Unconfirmed");
        }

        await using var withConfirmation = CreateContext(service, requireConfirmedAccount: true);
        withConfirmation.Render<AccountList>().Markup.Should().Contain("Unconfirmed");
    }
}
