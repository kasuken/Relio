using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Relio.Web.Components.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Settings;

public class SignInSecuritySettingsTests : BunitContext
{
    public SignInSecuritySettingsTests()
    {
        this.UseMudBlazor();
        AddAuthorization().SetAuthorized("marta@example.com");
    }

    [Fact]
    public void Shows_the_signed_in_email()
    {
        var cut = Render<CascadingAuthenticationState>(parameters => parameters
            .AddChildContent<SignInSecuritySettings>());

        cut.Find("[data-testid='settings-current-email']").TextContent.Trim().Should().Be("marta@example.com");
    }

    [Fact]
    public void Links_to_the_change_email_and_change_password_pages()
    {
        var cut = Render<CascadingAuthenticationState>(parameters => parameters
            .AddChildContent<SignInSecuritySettings>());

        cut.Find("[data-testid='settings-change-email']").GetAttribute("href").Should().Be("/Account/Manage/Email");
        cut.Find("[data-testid='settings-change-password']").GetAttribute("href")
            .Should().Be("/Account/Manage/ChangePassword");
    }
}
