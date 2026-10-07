using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relio.Application.Accounts;
using Relio.Application.Security;
using Relio.Data.Identity;
using Relio.Data.Tests.People;
using Relio.Data.Tests.Seeding;

namespace Relio.Data.Tests.Identity;

/// <summary>
/// Proves <see cref="TwoFactorStatusService"/> (issue #20) reports what Identity stores, only ever
/// for the signed-in user, and always from the database rather than from a tracked entity.
/// </summary>
public class TwoFactorStatusServiceTests
{
    private const string Password = "Correct-Horse-Battery-9";

    [Fact]
    public async Task Reports_off_with_no_codes_for_a_new_user()
    {
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var user = await CreateUserAsync(UserManagerTestFactory.Create(dbContext), "marta@example.com");

        var status = await new TwoFactorStatusService(dbContext, new FakeCurrentUser(user.Id)).GetStatusAsync();

        status.IsEnabled.Should().BeFalse();
        status.RecoveryCodesLeft.Should().Be(0);
        status.RecoveryCodesLow.Should().BeFalse();
    }

    [Fact]
    public async Task Reports_on_with_ten_codes_after_turning_two_factor_on()
    {
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager, "marta@example.com");
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.TurnOnTwoFactorAsync(user);

        var status = await new TwoFactorStatusService(dbContext, new FakeCurrentUser(user.Id)).GetStatusAsync();

        status.IsEnabled.Should().BeTrue();
        status.RecoveryCodesLeft.Should().Be(10);
    }

    [Fact]
    public async Task Reports_on_with_remaining_codes_after_one_is_redeemed()
    {
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager, "marta@example.com");
        await userManager.ResetAuthenticatorKeyAsync(user);
        var codes = (await userManager.TurnOnTwoFactorAsync(user))!;
        await userManager.RedeemTwoFactorRecoveryCodeAsync(user, codes[0]);

        var status = await new TwoFactorStatusService(dbContext, new FakeCurrentUser(user.Id)).GetStatusAsync();

        status.Should().Be(new TwoFactorStatus(true, 9));
    }

    [Fact]
    public async Task The_count_always_agrees_with_what_Identity_reports()
    {
        // The service reads Identity's token rows directly (see its remarks); this is the guard
        // against a future Identity release renaming them.
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = await CreateUserAsync(userManager, "marta@example.com");
        await userManager.ResetAuthenticatorKeyAsync(user);
        var service = new TwoFactorStatusService(dbContext, new FakeCurrentUser(user.Id));
        var codes = (await userManager.TurnOnTwoFactorAsync(user))!;

        foreach (var code in codes.Take(8))
        {
            await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
            (await service.GetStatusAsync()).RecoveryCodesLeft.Should().Be(await userManager.CountRecoveryCodesAsync(user));
        }

        (await service.GetStatusAsync()).RecoveryCodesLeft.Should().Be(2);
    }

    [Fact]
    public async Task Unknown_user_reports_off()
    {
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString());

        var status = await new TwoFactorStatusService(dbContext, new FakeCurrentUser("no-such-user")).GetStatusAsync();

        status.IsEnabled.Should().BeFalse();
        status.RecoveryCodesLeft.Should().Be(0);
    }

    [Fact]
    public async Task Requires_a_signed_in_user()
    {
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new TwoFactorStatusService(dbContext, new FakeCurrentUser(null));

        var act = () => service.GetStatusAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public async Task Does_not_report_another_users_two_factor_state()
    {
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var userManager = UserManagerTestFactory.Create(dbContext);
        var owner = await CreateUserAsync(userManager, "owner@example.com");
        var other = await CreateUserAsync(userManager, "other@example.com");
        await userManager.ResetAuthenticatorKeyAsync(owner);
        await userManager.TurnOnTwoFactorAsync(owner);

        var status = await new TwoFactorStatusService(dbContext, new FakeCurrentUser(other.Id)).GetStatusAsync();

        status.IsEnabled.Should().BeFalse();
        status.RecoveryCodesLeft.Should().Be(0);
    }

    [Fact]
    public async Task Reads_fresh_state_even_when_the_context_already_tracks_the_user()
    {
        // An interactive circuit keeps one DbContext for its whole life; the static pages change
        // two-factor state in other requests, i.e. other contexts. The service must still see it.
        var databaseName = Guid.NewGuid().ToString();
        await using var circuitContext = CreateDbContext(databaseName);
        await using var requestContext = CreateDbContext(databaseName);
        var circuitManager = UserManagerTestFactory.Create(circuitContext);
        var requestManager = UserManagerTestFactory.Create(requestContext);
        var user = await CreateUserAsync(circuitManager, "marta@example.com");
        var service = new TwoFactorStatusService(circuitContext, new FakeCurrentUser(user.Id));
        (await service.GetStatusAsync()).IsEnabled.Should().BeFalse();
        (await circuitManager.FindByIdAsync(user.Id)).Should().NotBeNull("the circuit's context now tracks the user");

        var requestUser = (await requestManager.FindByIdAsync(user.Id))!;
        await requestManager.ResetAuthenticatorKeyAsync(requestUser);
        await requestManager.TurnOnTwoFactorAsync(requestUser);

        var status = await service.GetStatusAsync();

        status.IsEnabled.Should().BeTrue();
        status.RecoveryCodesLeft.Should().Be(10);
    }

    private static async Task<RelioUser> CreateUserAsync(UserManager<RelioUser> userManager, string email)
    {
        var user = new RelioUser { UserName = email, Email = email };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user;
    }

    private static RelioDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new RelioDbContext(options, TimeProvider.System, FieldProtector);
    }
}
