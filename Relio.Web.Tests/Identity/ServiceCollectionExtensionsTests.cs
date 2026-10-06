using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Data.Identity;
using Relio.Web.Identity;

namespace Relio.Web.Tests.Identity;

/// <summary>
/// Covers how <c>Email:Provider</c> selects whether Identity requires a confirmed account -
/// see <see cref="ServiceCollectionExtensions.AddRelioIdentity"/>.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void RequiresConfirmedAccount_is_false_when_Email_Provider_is_not_set()
    {
        var configuration = BuildConfiguration([]);

        ServiceCollectionExtensions.RequiresConfirmedAccount(configuration).Should().BeFalse();
    }

    [Theory]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData("Unknown")]
    public void RequiresConfirmedAccount_is_false_for_none_or_unknown_providers(string provider)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["Email:Provider"] = provider });

        ServiceCollectionExtensions.RequiresConfirmedAccount(configuration).Should().BeFalse();
    }

    [Theory]
    [InlineData("Smtp")]
    [InlineData("smtp")]
    [InlineData("SMTP")]
    public void RequiresConfirmedAccount_is_true_for_smtp_regardless_of_casing(string provider)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["Email:Provider"] = provider });

        ServiceCollectionExtensions.RequiresConfirmedAccount(configuration).Should().BeTrue();
    }

    [Fact]
    public void BuildAccountOptions_defaults_match_the_issue_16_acceptance_criteria_when_unset()
    {
        var configuration = BuildConfiguration([]);

        var options = ServiceCollectionExtensions.BuildAccountOptions(configuration);

        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Cookie.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(14));
        options.PasswordReset.TokenLifespan.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void BuildAccountOptions_reads_overridden_values()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Account:Lockout:MaxFailedAccessAttempts"] = "3",
            ["Account:Lockout:DefaultLockoutTimeSpan"] = "00:05:00",
            ["Account:Lockout:AllowedForNewUsers"] = "false",
            ["Account:Cookie:ExpireTimeSpan"] = "1.00:00:00",
            ["Account:PasswordReset:TokenLifespan"] = "00:30:00",
        });

        var options = ServiceCollectionExtensions.BuildAccountOptions(configuration);

        options.Lockout.MaxFailedAccessAttempts.Should().Be(3);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
        options.Lockout.AllowedForNewUsers.Should().BeFalse();
        options.Cookie.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(1));
        options.PasswordReset.TokenLifespan.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void BuildAccountOptions_applies_defaults_to_properties_left_out_of_a_partial_override()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Account:Lockout:MaxFailedAccessAttempts"] = "10",
        });

        var options = ServiceCollectionExtensions.BuildAccountOptions(configuration);

        options.Lockout.MaxFailedAccessAttempts.Should().Be(10);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Cookie.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(14));
        options.PasswordReset.TokenLifespan.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void BuildAccountOptions_defaults_session_validation_interval_to_30_minutes()
    {
        ServiceCollectionExtensions.BuildAccountOptions(BuildConfiguration([]))
            .Session.ValidationInterval.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void BuildAccountOptions_reads_an_overridden_session_validation_interval()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Account:Session:ValidationInterval"] = "00:01:00",
        });

        ServiceCollectionExtensions.BuildAccountOptions(configuration)
            .Session.ValidationInterval.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void BuildRegistrationOptions_defaults_to_Open_with_a_7_day_invitation_lifetime()
    {
        var options = ServiceCollectionExtensions.BuildRegistrationOptions(BuildConfiguration([]));

        options.Mode.Should().Be(RegistrationMode.Open);
        options.InvitationLifetime.Should().Be(TimeSpan.FromDays(7));
    }

    [Theory]
    [InlineData("open", RegistrationMode.Open)]
    [InlineData("Open", RegistrationMode.Open)]
    [InlineData("InviteOnly", RegistrationMode.InviteOnly)]
    [InlineData("inviteonly", RegistrationMode.InviteOnly)]
    [InlineData("CLOSED", RegistrationMode.Closed)]
    [InlineData("  Closed  ", RegistrationMode.Closed)]
    [InlineData("", RegistrationMode.Open)]
    [InlineData("   ", RegistrationMode.Open)]
    public void BuildRegistrationOptions_parses_modes_case_insensitively(string value, RegistrationMode expected)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["Registration:Mode"] = value });

        ServiceCollectionExtensions.BuildRegistrationOptions(configuration).Mode.Should().Be(expected);
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("InviteOnyl")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("Open,Closed")]
    public void BuildRegistrationOptions_rejects_unknown_values(string value)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["Registration:Mode"] = value });

        var act = () => ServiceCollectionExtensions.BuildRegistrationOptions(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Registration:Mode*");
    }

    [Fact]
    public void BuildRegistrationOptions_reads_an_overridden_invitation_lifetime()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Registration:InvitationLifetime"] = "1.00:00:00",
        });

        ServiceCollectionExtensions.BuildRegistrationOptions(configuration)
            .InvitationLifetime.Should().Be(TimeSpan.FromDays(1));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-1.00:00:00")]
    [InlineData("soon")]
    public void BuildRegistrationOptions_rejects_a_non_positive_or_unparseable_invitation_lifetime(string value)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Registration:InvitationLifetime"] = value,
        });

        var act = () => ServiceCollectionExtensions.BuildRegistrationOptions(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Registration:InvitationLifetime*");
    }

    [Fact]
    public void AddRelioIdentity_fails_fast_on_an_unknown_registration_mode()
    {
        var act = () => IdentityServicesFactory.Build(new Dictionary<string, string?> { ["Registration:Mode"] = "Nope" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Registration:Mode*");
    }

    [Fact]
    public async Task AddRelioIdentity_registers_role_support_the_custom_sign_in_manager_and_the_Administrator_policy()
    {
        await using var provider = IdentityServicesFactory.Build(new Dictionary<string, string?>
        {
            ["Registration:Mode"] = "InviteOnly",
            ["Account:Session:ValidationInterval"] = "00:02:00",
        });
        using var scope = IdentityServicesFactory.CreateScopeWithHttpContext(provider);

        scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>().SupportsUserRole.Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<SignInManager<RelioUser>>().Should().BeOfType<RelioSignInManager>();

        var policy = await scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(RelioPolicies.Administrator);
        policy.Should().NotBeNull();
        policy!.Requirements.OfType<RolesAuthorizationRequirement>().Should().ContainSingle()
            .Which.AllowedRoles.Should().Equal(RelioRoles.Administrator);

        scope.ServiceProvider.GetRequiredService<IOptions<RegistrationOptions>>().Value.Mode
            .Should().Be(RegistrationMode.InviteOnly);
        scope.ServiceProvider.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value.ValidationInterval
            .Should().Be(TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Production", CookieSecurePolicy.Always)]
    public async Task Two_factor_cookies_are_http_only_lax_and_secure_outside_development(
        string environmentName, CookieSecurePolicy expectedSecurePolicy)
    {
        await using var provider = IdentityServicesFactory.Build(environmentName: environmentName);
        var cookies = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        foreach (var scheme in new[] { IdentityConstants.TwoFactorUserIdScheme, IdentityConstants.TwoFactorRememberMeScheme })
        {
            var cookie = cookies.Get(scheme).Cookie;
            cookie.HttpOnly.Should().BeTrue(scheme);
            cookie.SameSite.Should().Be(SameSiteMode.Lax, scheme);
            cookie.SecurePolicy.Should().Be(expectedSecurePolicy, scheme);
        }
    }

    [Fact]
    public void The_two_factor_user_id_cookie_keeps_Identitys_short_five_minute_lifetime()
    {
        using var provider = IdentityServicesFactory.Build();

        provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.TwoFactorUserIdScheme).ExpireTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Authenticator_token_provider_is_registered()
    {
        await using var provider = IdentityServicesFactory.Build();
        using var scope = IdentityServicesFactory.CreateScopeWithHttpContext(provider);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();

        userManager.Options.Tokens.AuthenticatorTokenProvider.Should().Be(TokenOptions.DefaultAuthenticatorProvider);
        userManager.Options.Tokens.ProviderMap.Should().ContainKey(TokenOptions.DefaultAuthenticatorProvider);
        userManager.SupportsUserTwoFactor.Should().BeTrue();
        userManager.SupportsUserAuthenticatorKey.Should().BeTrue();
        userManager.SupportsUserTwoFactorRecoveryCodes.Should().BeTrue();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
