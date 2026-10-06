using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Accounts;
using Relio.Web.Components.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Settings;

public class SignInSecuritySettingsTests : BunitContext
{
    private readonly FakeTwoFactorStatusService _twoFactor = new();

    public SignInSecuritySettingsTests()
    {
        this.UseMudBlazor();
        AddAuthorization().SetAuthorized("marta@example.com");
        Services.AddSingleton<ITwoFactorStatusService>(_twoFactor);
    }

    private IRenderedComponent<CascadingAuthenticationState> RenderSection() =>
        Render<CascadingAuthenticationState>(parameters => parameters
            .AddChildContent<SignInSecuritySettings>());

    [Fact]
    public void Shows_the_signed_in_email()
    {
        var cut = RenderSection();

        cut.Find("[data-testid='settings-current-email']").TextContent.Trim().Should().Be("marta@example.com");
    }

    [Fact]
    public void Links_to_the_change_email_and_change_password_pages()
    {
        var cut = RenderSection();

        cut.Find("[data-testid='settings-change-email']").GetAttribute("href").Should().Be("/Account/Manage/Email");
        cut.Find("[data-testid='settings-change-password']").GetAttribute("href")
            .Should().Be("/Account/Manage/ChangePassword");
    }

    [Fact]
    public void Shows_two_factor_off_with_a_set_up_link()
    {
        var cut = RenderSection();

        cut.Find("[data-testid='settings-2fa-state']").TextContent.Trim().Should().Be("Off");
        var link = cut.Find("[data-testid='settings-2fa-manage']");
        link.TextContent.Trim().Should().Be("Set up two-factor authentication");
        link.GetAttribute("href").Should().Be("/Account/Manage/TwoFactorAuthentication");
        link.GetAttribute("data-enhance-nav").Should().Be("false");
        cut.FindAll("[data-testid='settings-2fa-recovery-codes-low']").Should().BeEmpty();
    }

    [Fact]
    public void Shows_two_factor_on_with_a_manage_link()
    {
        _twoFactor.Status = new TwoFactorStatus(IsEnabled: true, RecoveryCodesLeft: 10);

        var cut = RenderSection();

        cut.Find("[data-testid='settings-2fa-state']").TextContent.Trim().Should().Be("On");
        cut.Find("[data-testid='settings-2fa-manage']").TextContent.Trim().Should().Be("Manage two-factor authentication");
    }

    [Theory]
    [InlineData(3, "3 recovery codes")]
    [InlineData(1, "1 recovery code ")]
    [InlineData(0, "0 recovery codes")]
    public void Warns_when_recovery_codes_are_low(int codesLeft, string expectedText)
    {
        _twoFactor.Status = new TwoFactorStatus(IsEnabled: true, codesLeft);

        var cut = RenderSection();

        cut.Find("[data-testid='settings-2fa-recovery-codes-low']").TextContent.Should().Contain(expectedText);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public void Does_not_warn_with_enough_codes(int codesLeft)
    {
        _twoFactor.Status = new TwoFactorStatus(IsEnabled: true, codesLeft);

        var cut = RenderSection();

        cut.FindAll("[data-testid='settings-2fa-recovery-codes-low']").Should().BeEmpty();
    }
}
