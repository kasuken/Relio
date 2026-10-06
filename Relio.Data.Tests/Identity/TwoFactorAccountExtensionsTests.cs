using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Identity;
using Relio.Data.Tests.Seeding;

namespace Relio.Data.Tests.Identity;

/// <summary>
/// Proves what turning two-factor authentication on and off (issue #20) does to an account, through
/// a real <see cref="UserManager{TUser}"/>: <see cref="TwoFactorAccountExtensions"/> is the one place
/// the pages go through, so these tests are the contract for them.
/// </summary>
public partial class TwoFactorAccountExtensionsTests
{
    private const string Password = "Correct-Horse-Battery-9";

    [GeneratedRegex("^[2-9BCDFGHJKMNPQRTVWXY]{5}-[2-9BCDFGHJKMNPQRTVWXY]{5}$")]
    private static partial Regex RecoveryCodeShape();

    [Fact]
    public async Task TurnOnTwoFactorAsync_enables_returns_ten_unique_codes_and_rotates_the_stamp()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserWithKeyAsync(userManager);
        var stampBefore = await userManager.GetSecurityStampAsync(user);

        var codes = await userManager.TurnOnTwoFactorAsync(user);

        codes.Should().NotBeNull();
        codes!.Should().HaveCount(TwoFactorAccountExtensions.RecoveryCodeCount).And.OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => RecoveryCodeShape().IsMatch(c));
        (await userManager.GetTwoFactorEnabledAsync(user)).Should().BeTrue();
        (await userManager.CountRecoveryCodesAsync(user)).Should().Be(10);
        (await userManager.GetSecurityStampAsync(user)).Should().NotBe(stampBefore);
    }

    [Fact]
    public async Task TurnOnTwoFactorAsync_invalidates_previous_recovery_codes()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserWithKeyAsync(userManager);
        var first = (await userManager.TurnOnTwoFactorAsync(user))!;

        var second = (await userManager.TurnOnTwoFactorAsync(user))!;

        second.Should().NotIntersectWith(first);
        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, first[0])).Succeeded.Should().BeFalse();
        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, second[0])).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task TurnOffTwoFactorAsync_disables_rotates_the_key_and_clears_recovery_codes()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserWithKeyAsync(userManager);
        var codes = (await userManager.TurnOnTwoFactorAsync(user))!;
        var keyBefore = await userManager.GetAuthenticatorKeyAsync(user);
        var stampBefore = await userManager.GetSecurityStampAsync(user);

        var result = await userManager.TurnOffTwoFactorAsync(user);

        result.Succeeded.Should().BeTrue();
        (await userManager.GetTwoFactorEnabledAsync(user)).Should().BeFalse();
        (await userManager.GetAuthenticatorKeyAsync(user)).Should().NotBeNullOrEmpty().And.NotBe(keyBefore);
        (await userManager.CountRecoveryCodesAsync(user)).Should().Be(0);
        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded.Should().BeFalse();
        (await userManager.GetSecurityStampAsync(user)).Should().NotBe(stampBefore);
    }

    [Fact]
    public async Task Turning_off_then_on_again_never_revives_the_old_key_or_codes()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserWithKeyAsync(userManager);
        var oldKey = await userManager.GetAuthenticatorKeyAsync(user);
        var oldCodes = (await userManager.TurnOnTwoFactorAsync(user))!;
        await userManager.TurnOffTwoFactorAsync(user);

        var newCodes = (await userManager.TurnOnTwoFactorAsync(user))!;

        (await userManager.GetAuthenticatorKeyAsync(user)).Should().NotBe(oldKey);
        newCodes.Should().NotIntersectWith(oldCodes);
    }

    private static async Task<RelioUser> CreateUserWithKeyAsync(UserManager<RelioUser> userManager)
    {
        var user = new RelioUser { UserName = "marta@example.com", Email = "marta@example.com" };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        (await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
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
