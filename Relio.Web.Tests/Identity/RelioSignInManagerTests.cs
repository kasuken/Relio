using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Web.Identity;

namespace Relio.Web.Tests.Identity;

/// <summary>
/// Proves issue #19's disabled-account rules through <see cref="RelioSignInManager"/> wired exactly
/// as the app wires it. The key property: a disabled account is only ever announced after the
/// password was verified, so the sign-in form can not be used to learn which accounts exist.
/// </summary>
public class RelioSignInManagerTests
{
    private const string Password = "Str0ng-Passw0rd!";

    private static async Task<(ServiceProvider Provider, IServiceScope Scope, RelioUser User)> ArrangeAsync(bool disabled)
    {
        var provider = IdentityServicesFactory.Build();
        var scope = IdentityServicesFactory.CreateScopeWithHttpContext(provider);
        await scope.ServiceProvider.GetRequiredService<RelioDbContext>().Database.EnsureCreatedAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
        var user = new RelioUser { UserName = "member@example.com", Email = "member@example.com", EmailConfirmed = true, IsDisabled = disabled };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return (provider, scope, user);
    }

    [Fact]
    public async Task Sign_in_manager_is_the_custom_one()
    {
        var (provider, scope, _) = await ArrangeAsync(disabled: false);
        using (provider)
        using (scope)
        {
            scope.ServiceProvider.GetRequiredService<SignInManager<RelioUser>>().Should().BeOfType<RelioSignInManager>();
        }
    }

    [Fact]
    public async Task Disabled_user_with_correct_password_gets_the_Disabled_result()
    {
        var (provider, scope, user) = await ArrangeAsync(disabled: true);
        using (provider)
        using (scope)
        {
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<RelioUser>>();

            var result = await signInManager.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: true);

            result.Succeeded.Should().BeFalse();
            result.Should().BeOfType<RelioSignInResult>().Which.IsDisabled.Should().BeTrue();
            result.IsNotAllowed.Should().BeTrue("anything that only knows Identity's result still sees a refused sign-in");
            result.IsLockedOut.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Disabled_user_with_wrong_password_gets_a_plain_failure_and_counts_towards_lockout()
    {
        var (provider, scope, user) = await ArrangeAsync(disabled: true);
        using (provider)
        using (scope)
        {
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<RelioUser>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();

            var result = await signInManager.PasswordSignInAsync(user, "Wr0ng-Passw0rd!", isPersistent: false, lockoutOnFailure: true);

            // Exactly what an enabled account answers to a wrong password: nothing about "disabled".
            result.Should().NotBeOfType<RelioSignInResult>();
            result.Succeeded.Should().BeFalse();
            result.IsNotAllowed.Should().BeFalse();
            result.IsLockedOut.Should().BeFalse();
            (await userManager.GetAccessFailedCountAsync(user)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Enabled_user_is_unaffected()
    {
        var (provider, scope, user) = await ArrangeAsync(disabled: false);
        using (provider)
        using (scope)
        {
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<RelioUser>>();

            var result = await signInManager.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: true);

            result.Succeeded.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ValidateSecurityStampAsync_rejects_a_disabled_user_even_with_a_matching_stamp()
    {
        var (provider, scope, user) = await ArrangeAsync(disabled: false);
        using (provider)
        using (scope)
        {
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<RelioUser>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
            var principal = await signInManager.CreateUserPrincipalAsync(user);

            (await signInManager.ValidateSecurityStampAsync(principal)).Should().NotBeNull("an enabled user's session is valid");

            user.IsDisabled = true;
            (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();

            (await signInManager.ValidateSecurityStampAsync(principal)).Should().BeNull();
        }
    }

    [Fact]
    public async Task A_disabled_account_never_receives_a_sign_in_cookie()
    {
        var (provider, scope, user) = await ArrangeAsync(disabled: true);
        using (provider)
        using (scope)
        {
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<RelioUser>>();
            var context = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;

            await signInManager.SignInAsync(user, isPersistent: false);

            context.Response.Headers.SetCookie.ToString().Should().BeEmpty();
        }
    }
}
