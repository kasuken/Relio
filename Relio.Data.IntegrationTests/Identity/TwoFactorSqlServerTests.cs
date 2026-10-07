using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Accounts;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.Identity;

/// <summary>
/// Proves what only a real SQL Server can for issue #20: the authenticator key and recovery codes
/// survive a round trip through the real migrations (Identity keeps them in <c>AspNetUserTokens</c>,
/// so two-factor authentication needs no migration of its own), and <see cref="TwoFactorStatusService"/>
/// reads them back correctly from SQL Server.
/// </summary>
/// <remarks>
/// The database is shared by every test in the run (see <see cref="SqlServerDatabaseFixture"/>), so
/// each test creates its own user with a unique email and asserts only about that user.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class TwoFactorSqlServerTests(SqlServerDatabaseFixture fixture)
{
    private const string Password = "Correct-Horse-Battery-9";

    [SqlServerFact]
    public async Task Authenticator_key_and_recovery_codes_round_trip_through_the_real_migrations()
    {
        var email = $"2fa-{Guid.NewGuid():N}@example.com";
        string userId;
        string key;
        IReadOnlyList<string> codes;

        await using (var writeContext = fixture.CreateDbContext())
        {
            var userManager = CreateUserManager(writeContext);
            var user = new RelioUser { UserName = email, Email = email };
            (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            (await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
            key = (await userManager.GetAuthenticatorKeyAsync(user))!;
            codes = (await userManager.TurnOnTwoFactorAsync(user))!;
            userId = user.Id;

            var rawTokenValues = await writeContext.Database.SqlQueryRaw<string>(
                    "SELECT [Value] AS [Value] FROM [AspNetUserTokens] WHERE [UserId] = {0}",
                    userId)
                .ToListAsync();
            rawTokenValues.Should().HaveCount(2);
            rawTokenValues.Should().NotContain(key);
            rawTokenValues.Should().NotContain(string.Join(";", codes));
        }

        // A brand new context and manager: nothing is served from memory.
        await using (var readContext = fixture.CreateDbContext())
        {
            var userManager = CreateUserManager(readContext);
            var user = (await userManager.FindByIdAsync(userId))!;

            user.TwoFactorEnabled.Should().BeTrue();
            (await userManager.GetAuthenticatorKeyAsync(user)).Should().Be(key);
            (await userManager.CountRecoveryCodesAsync(user)).Should().Be(10);

            (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded.Should().BeTrue();
            (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded.Should().BeFalse();
        }

        await using var statusContext = fixture.CreateDbContext();
        var status = await new TwoFactorStatusService(statusContext, new FakeCurrentUser(userId)).GetStatusAsync();
        status.Should().Be(new TwoFactorStatus(IsEnabled: true, RecoveryCodesLeft: 9));
    }

    [SqlServerFact]
    public async Task Turning_two_factor_off_clears_the_codes_in_SQL_Server()
    {
        var email = $"2fa-off-{Guid.NewGuid():N}@example.com";
        string userId;

        await using (var writeContext = fixture.CreateDbContext())
        {
            var userManager = CreateUserManager(writeContext);
            var user = new RelioUser { UserName = email, Email = email };
            (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            (await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
            await userManager.TurnOnTwoFactorAsync(user);
            (await userManager.TurnOffTwoFactorAsync(user)).Succeeded.Should().BeTrue();
            userId = user.Id;
        }

        await using var statusContext = fixture.CreateDbContext();
        var status = await new TwoFactorStatusService(statusContext, new FakeCurrentUser(userId)).GetStatusAsync();
        status.Should().Be(new TwoFactorStatus(IsEnabled: false, RecoveryCodesLeft: 0));
    }

    private static UserManager<RelioUser> CreateUserManager(RelioDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddLogging();
        ConfigureDataProtection(services);
        services.AddIdentityCore<RelioUser>(options => options.User.RequireUniqueEmail = true)
            // Before the stores (like AddRelioIdentity): otherwise role calls throw NotSupportedException.
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<RelioDbContext>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider().GetRequiredService<UserManager<RelioUser>>();
    }
}
