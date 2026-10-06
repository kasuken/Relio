using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Web.E2ETests.Infrastructure;
using Relio.Web.Identity;

namespace Relio.Web.Tests.Identity;

/// <summary>
/// Proves issue #20's sign-in rules through <see cref="RelioSignInManager"/> wired exactly as the app
/// wires it, with the two-factor cookie held by a <see cref="FakeAuthenticationService"/>: each step
/// (password, then code) runs in its own scope, like the two requests of a real sign-in. The key
/// properties: one lockout budget covers password, authenticator-code and recovery-code guesses; a
/// locked-out account cannot use even a valid recovery code; and a disabled account is refused at
/// the code step too, with its half-finished sign-in cleared.
/// </summary>
public class RelioSignInManagerTwoFactorTests
{
    private const string Password = "Str0ng-Passw0rd!";
    private const string WrongPassword = "Wr0ng-Passw0rd!";

    private sealed record Arrangement(
        ServiceProvider Provider, FakeAuthenticationService Auth, string UserId, string Key, IReadOnlyList<string> RecoveryCodes);

    private static async Task<Arrangement> ArrangeAsync(bool disabled = false)
    {
        var auth = new FakeAuthenticationService();
        var provider = IdentityServicesFactory.Build(configureServices: services => services.AddSingleton<IAuthenticationService>(auth));

        using var scope = IdentityServicesFactory.CreateScopeWithHttpContext(provider);
        await scope.ServiceProvider.GetRequiredService<RelioDbContext>().Database.EnsureCreatedAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();

        var user = new RelioUser { UserName = "member@example.com", Email = "member@example.com", EmailConfirmed = true };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        (await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
        var key = (await userManager.GetAuthenticatorKeyAsync(user))!;
        var codes = (await userManager.TurnOnTwoFactorAsync(user))!;

        if (disabled)
        {
            user.IsDisabled = true;
            (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();
        }

        return new Arrangement(provider, auth, user.Id, key, codes);
    }

    /// <summary>Runs <paramref name="step"/> in a fresh scope (a fresh request) with the user loaded fresh.</summary>
    private static async Task<T> InNewRequestAsync<T>(
        Arrangement arrangement, Func<RelioSignInManager, UserManager<RelioUser>, RelioUser, Task<T>> step)
    {
        using var scope = IdentityServicesFactory.CreateScopeWithHttpContext(arrangement.Provider);
        var signInManager = (RelioSignInManager)scope.ServiceProvider.GetRequiredService<SignInManager<RelioUser>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
        var user = (await userManager.FindByIdAsync(arrangement.UserId))!;
        return await step(signInManager, userManager, user);
    }

    private static Task<SignInResult> PasswordStepAsync(Arrangement a, string password = Password) =>
        InNewRequestAsync(a, (signIn, _, user) => signIn.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true));

    private static Task<SignInResult> AuthenticatorStepAsync(Arrangement a, string code, bool isPersistent = false) =>
        InNewRequestAsync(a, (signIn, _, _) => signIn.TwoFactorAuthenticatorSignInAsync(code, isPersistent, rememberClient: false));

    private static Task<SignInResult> RecoveryStepAsync(Arrangement a, string code) =>
        InNewRequestAsync(a, (signIn, _, _) => signIn.TwoFactorRecoveryCodeSignInAsync(code));

    private static Task<int> FailedCountAsync(Arrangement a) =>
        InNewRequestAsync(a, (_, userManager, user) => userManager.GetAccessFailedCountAsync(user));

    private static string WrongAuthenticatorCode(Arrangement a) => Totp.CreateWrongCode(a.Key, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Two_factor_user_with_correct_password_gets_TwoFactorRequired()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            var result = await PasswordStepAsync(arrangement);

            result.Succeeded.Should().BeFalse();
            result.RequiresTwoFactor.Should().BeTrue();
            arrangement.Auth.HasPendingTwoFactorStep.Should().BeTrue();
            arrangement.Auth.SignedInWith(IdentityConstants.TwoFactorUserIdScheme).Should().BeTrue();
            arrangement.Auth.SignedInWith(IdentityConstants.ApplicationScheme).Should().BeFalse("no session before the code");
        }
    }

    [Fact]
    public async Task Disabled_two_factor_user_gets_Disabled_and_no_two_factor_cookie()
    {
        var arrangement = await ArrangeAsync(disabled: true);
        using (arrangement.Provider)
        {
            var result = await PasswordStepAsync(arrangement);

            result.Should().BeOfType<RelioSignInResult>().Which.IsDisabled.Should().BeTrue();
            result.RequiresTwoFactor.Should().BeFalse();
            arrangement.Auth.HasPendingTwoFactorStep.Should().BeFalse("a disabled account never reaches the code step");
        }
    }

    [Fact]
    public async Task A_valid_authenticator_code_completes_the_sign_in_and_clears_the_two_factor_cookie()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            await PasswordStepAsync(arrangement);

            var result = await AuthenticatorStepAsync(arrangement, Totp.Compute(arrangement.Key, DateTimeOffset.UtcNow));

            result.Succeeded.Should().BeTrue();
            arrangement.Auth.HasPendingTwoFactorStep.Should().BeFalse();
            arrangement.Auth.ApplicationSignIns.Should().ContainSingle()
                .Which.HasClaim("amr", "mfa").Should().BeTrue();
        }
    }

    [Fact]
    public async Task Authenticator_sign_in_honours_remember_me_only_through_the_persistence_flag_never_a_remembered_device()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            await PasswordStepAsync(arrangement);

            await AuthenticatorStepAsync(arrangement, Totp.Compute(arrangement.Key, DateTimeOffset.UtcNow), isPersistent: true);

            arrangement.Auth.SignedInWith(IdentityConstants.TwoFactorRememberMeScheme)
                .Should().BeFalse("Relio has no 'remember this device'");
        }
    }

    [Theory]
    [InlineData("authenticator")]
    [InlineData("provider")]
    [InlineData("recovery")]
    public async Task User_disabled_during_the_code_step_is_refused_and_the_two_factor_cookie_cleared(string method)
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            (await PasswordStepAsync(arrangement)).RequiresTwoFactor.Should().BeTrue();
            await InNewRequestAsync(arrangement, async (_, userManager, user) =>
            {
                user.IsDisabled = true;
                (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();
                return 0;
            });

            // Even a perfectly valid code must not complete the sign-in.
            var result = method switch
            {
                "authenticator" => await AuthenticatorStepAsync(arrangement, Totp.Compute(arrangement.Key, DateTimeOffset.UtcNow)),
                "provider" => await InNewRequestAsync(arrangement, (signIn, userManager, _) => signIn.TwoFactorSignInAsync(
                    userManager.Options.Tokens.AuthenticatorTokenProvider,
                    Totp.Compute(arrangement.Key, DateTimeOffset.UtcNow),
                    isPersistent: false,
                    rememberClient: false)),
                _ => await RecoveryStepAsync(arrangement, arrangement.RecoveryCodes[0]),
            };

            result.Should().BeOfType<RelioSignInResult>().Which.IsDisabled.Should().BeTrue();
            arrangement.Auth.HasPendingTwoFactorStep.Should().BeFalse("the half-finished sign-in is ended");
            arrangement.Auth.SignOuts.Should().Contain(IdentityConstants.TwoFactorUserIdScheme);
            arrangement.Auth.ApplicationSignIns.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Wrong_authenticator_codes_count_towards_lockout()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            await PasswordStepAsync(arrangement);

            for (var attempt = 1; attempt < 5; attempt++)
            {
                (await AuthenticatorStepAsync(arrangement, WrongAuthenticatorCode(arrangement))).Succeeded.Should().BeFalse();
            }

            (await AuthenticatorStepAsync(arrangement, WrongAuthenticatorCode(arrangement))).IsLockedOut.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Wrong_recovery_codes_count_towards_lockout()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            await PasswordStepAsync(arrangement);

            for (var attempt = 1; attempt < 5; attempt++)
            {
                var result = await RecoveryStepAsync(arrangement, "AAAAA-BBBBB");
                result.Succeeded.Should().BeFalse();
                result.IsLockedOut.Should().BeFalse();
            }

            (await FailedCountAsync(arrangement)).Should().Be(4);
            (await RecoveryStepAsync(arrangement, "AAAAA-BBBBB")).IsLockedOut.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Locked_out_user_cannot_use_a_valid_recovery_code()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            await PasswordStepAsync(arrangement);
            await InNewRequestAsync(arrangement, async (_, userManager, user) =>
            {
                (await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1))).Succeeded.Should().BeTrue();
                return 0;
            });

            var result = await RecoveryStepAsync(arrangement, arrangement.RecoveryCodes[0]);

            result.IsLockedOut.Should().BeTrue();
            arrangement.Auth.ApplicationSignIns.Should().BeEmpty();
            (await InNewRequestAsync(arrangement, (_, userManager, user) => userManager.CountRecoveryCodesAsync(user)))
                .Should().Be(10, "a refused attempt must not consume the code");
        }
    }

    [Fact]
    public async Task Valid_recovery_code_signs_in_once()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            await PasswordStepAsync(arrangement);
            var code = arrangement.RecoveryCodes[0];

            (await RecoveryStepAsync(arrangement, code)).Succeeded.Should().BeTrue();
            arrangement.Auth.ApplicationSignIns.Should().ContainSingle();
            arrangement.Auth.HasPendingTwoFactorStep.Should().BeFalse();

            // A second sign-in with the same code: password step again, then the used code.
            await PasswordStepAsync(arrangement);
            var second = await RecoveryStepAsync(arrangement, code);

            second.Succeeded.Should().BeFalse();
            arrangement.Auth.ApplicationSignIns.Should().ContainSingle("the used code signed nobody in");
        }
    }

    [Fact]
    public async Task A_successful_two_factor_sign_in_resets_the_failed_attempts()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            await PasswordStepAsync(arrangement);
            await RecoveryStepAsync(arrangement, "AAAAA-BBBBB");
            await RecoveryStepAsync(arrangement, "AAAAA-BBBBB");
            (await FailedCountAsync(arrangement)).Should().Be(2);

            (await RecoveryStepAsync(arrangement, arrangement.RecoveryCodes[0])).Succeeded.Should().BeTrue();

            (await FailedCountAsync(arrangement)).Should().Be(0);
        }
    }

    [Fact]
    public async Task Password_code_and_recovery_code_failures_share_one_lockout_budget()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            // Two wrong passwords, two wrong authenticator codes and one wrong recovery code: five
            // failures of three different kinds lock the account.
            await PasswordStepAsync(arrangement, WrongPassword);
            await PasswordStepAsync(arrangement, WrongPassword);
            (await PasswordStepAsync(arrangement)).RequiresTwoFactor.Should().BeTrue();

            (await AuthenticatorStepAsync(arrangement, WrongAuthenticatorCode(arrangement))).IsLockedOut.Should().BeFalse();
            (await AuthenticatorStepAsync(arrangement, WrongAuthenticatorCode(arrangement))).IsLockedOut.Should().BeFalse();
            (await FailedCountAsync(arrangement)).Should().Be(4);

            (await RecoveryStepAsync(arrangement, "AAAAA-BBBBB")).IsLockedOut.Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_correct_password_alone_does_not_reset_the_failed_attempts_of_a_two_factor_account()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            await PasswordStepAsync(arrangement, WrongPassword);
            await PasswordStepAsync(arrangement, WrongPassword);

            (await PasswordStepAsync(arrangement)).RequiresTwoFactor.Should().BeTrue();

            (await FailedCountAsync(arrangement)).Should().Be(2, "knowing the password must not refill the guess budget");
        }
    }

    [Fact]
    public async Task Code_steps_without_a_pending_sign_in_fail_without_touching_any_account()
    {
        var arrangement = await ArrangeAsync();
        using (arrangement.Provider)
        {
            (await RecoveryStepAsync(arrangement, arrangement.RecoveryCodes[0])).Succeeded.Should().BeFalse();
            (await AuthenticatorStepAsync(arrangement, Totp.Compute(arrangement.Key, DateTimeOffset.UtcNow))).Succeeded.Should().BeFalse();

            (await FailedCountAsync(arrangement)).Should().Be(0);
            arrangement.Auth.ApplicationSignIns.Should().BeEmpty();
        }
    }
}
