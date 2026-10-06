using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Administration;
using Relio.Application.Time;
using Relio.Web.Components.Administration;
using Relio.Web.Components.Pages;
using Relio.Web.Identity;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Administration;

public class InvitationPanelTests
{
    private static BunitContext CreateContext(
        FakeUserAdministrationService service, RegistrationMode mode = RegistrationMode.InviteOnly)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddOptions();
        context.Services.Configure<AccountOptions>(_ => { });
        context.Services.Configure<Microsoft.AspNetCore.Identity.IdentityOptions>(_ => { });
        context.Services.Configure<RegistrationOptions>(o => o.Mode = mode);
        context.Services.AddSingleton<IUserAdministrationService>(service);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService());
        context.Render<MudPopoverProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Created_invitation_link_points_at_register_with_only_the_token()
    {
        var service = new FakeUserAdministrationService { NextToken = "abc-DEF_123" };
        await using var context = CreateContext(service);
        var cut = context.Render<InvitationPanel>();

        cut.Find("input").Input("friend@example.com");
        cut.Find("[data-testid='admin-invite-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find("[data-testid='admin-invite-link']").TextContent.Trim();
            link.Should().Be("http://localhost/Account/Register?invite=abc-DEF_123");
            link.Should().NotContain("friend").And.NotContain("%40").And.NotContain("@");
        });
        service.InvitedEmails.Should().Equal("friend@example.com");
        cut.Find("[data-testid='admin-invite-created']").TextContent.Should().Contain("friend@example.com")
            .And.Contain("Relio won't show it again");
    }

    [Fact]
    public async Task Shows_a_calm_message_for_an_address_that_already_has_an_account()
    {
        var service = new FakeUserAdministrationService { NextCreateStatus = CreateInvitationStatus.AlreadyHasAccount };
        await using var context = CreateContext(service);
        var cut = context.Render<InvitationPanel>();

        cut.Find("input").Input("member@example.com");
        cut.Find("[data-testid='admin-invite-submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("An account with this email address already exists."));
        cut.FindAll("[data-testid='admin-invite-link']").Should().BeEmpty();
    }

    [Fact]
    public async Task Lists_pending_invitations_with_a_human_expiry_date()
    {
        var service = new FakeUserAdministrationService();
        service.Invitations.Add(new InvitationSummary(
            Guid.NewGuid(), "waiting@example.com", new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 8, 9, 0, 0, DateTimeKind.Utc)));
        await using var context = CreateContext(service);

        var cut = context.Render<InvitationPanel>();

        cut.Find("[data-testid='admin-invitation-email']").TextContent.Should().Be("waiting@example.com");
        cut.Find("[data-testid='admin-invitations']").TextContent.Should().Contain("Expires 8 March 2026");
        cut.FindAll("[data-testid='admin-invitation-revoke']").Should().ContainSingle();
    }

    [Theory]
    [InlineData(RegistrationMode.Open, false)]
    [InlineData(RegistrationMode.Closed, false)]
    [InlineData(RegistrationMode.InviteOnly, true)]
    public async Task Invitation_panel_is_hidden_unless_InviteOnly(RegistrationMode mode, bool shown)
    {
        await using var context = CreateContext(new FakeUserAdministrationService(), mode);

        var cut = context.Render<AdminUsers>();

        cut.FindAll("[data-testid='admin-invite-submit']").Any().Should().Be(shown);
        cut.Find("[data-testid='admin-registration-mode']").TextContent.Should().NotBeNullOrWhiteSpace();
    }
}
