using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Identity;
using Relio.Data.Tests.Seeding;

namespace Relio.Data.Tests.Identity;

/// <summary>
/// Pins down the ASP.NET Core Identity behaviour issue #20's design relies on (see "Two-factor
/// authentication" in AGENTS.md), against a real <see cref="UserManager{TUser}"/>, so a framework
/// upgrade that changes any of it fails a test instead of silently weakening the feature: which
/// operations rotate the security stamp (and so need <c>RefreshSignInAsync</c>), recovery codes
/// being single use, and a password reset leaving two-factor authentication on.
/// </summary>
public class TwoFactorIdentityBehaviourTests
{
    private const string Password = "Correct-Horse-Battery-9";
    private const string NewPassword = "Another-Horse-Battery-7";

    [Fact]
    public async Task ResetAuthenticatorKeyAsync_rotates_the_security_stamp()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager);
        var stampBefore = await userManager.GetSecurityStampAsync(user);

        (await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();

        (await userManager.GetSecurityStampAsync(user)).Should().NotBe(stampBefore);
    }

    [Fact]
    public async Task Generating_recovery_codes_does_not_rotate_the_security_stamp()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager);
        var stampBefore = await userManager.GetSecurityStampAsync(user);

        await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        (await userManager.GetSecurityStampAsync(user)).Should().Be(stampBefore);
    }

    [Fact]
    public async Task A_recovery_code_redeems_only_once()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager);
        var code = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.First();

        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded.Should().BeTrue();
        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Redeeming_a_code_decrements_the_count()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager);
        var code = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.First();

        await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);

        (await userManager.CountRecoveryCodesAsync(user)).Should().Be(9);
    }

    [Fact]
    public async Task Recovery_codes_are_matched_exactly_so_the_pages_must_normalise_case()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager);
        var code = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.First();

        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code.ToLowerInvariant())).Succeeded.Should().BeFalse();
        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task The_authenticator_token_provider_cannot_generate_a_code_so_tests_compute_their_own()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager);
        await userManager.ResetAuthenticatorKeyAsync(user);

        var generated = await userManager.GenerateTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider);

        generated.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetPasswordAsync_keeps_two_factor_enabled_and_the_authenticator_key()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.TurnOnTwoFactorAsync(user);
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var result = await userManager.ResetPasswordAsync(user, token, NewPassword);

        result.Succeeded.Should().BeTrue();
        (await userManager.GetTwoFactorEnabledAsync(user)).Should().BeTrue(
            "forgetting the password must never be a way around the second factor");
        (await userManager.GetAuthenticatorKeyAsync(user)).Should().Be(key);
        (await userManager.CountRecoveryCodesAsync(user)).Should().Be(10);
    }

    private static async Task<RelioUser> CreateUserAsync(UserManager<RelioUser> userManager)
    {
        var user = new RelioUser { UserName = "marta@example.com", Email = "marta@example.com" };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user;
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RelioDbContext(options, TimeProvider.System);
    }
}
