using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Data.Administration;
using Relio.Data.Identity;
using Relio.Data.Tests.People;
using Relio.Data.Tests.Seeding;

namespace Relio.Data.Tests.Administration;

/// <summary>
/// Builds the real services of issue #19 against the EF Core InMemory provider, with a real
/// <see cref="UserManager{TUser}"/> (see <see cref="UserManagerTestFactory"/>) so password policy,
/// unique email and roles behave as in the app. Every call that takes the same
/// <see cref="DbContextOptions{TContext}"/> sees the same database.
/// </summary>
internal static class AdministrationTestHarness
{
    public const string StrongPassword = "Str0ng-Passw0rd!";

    public static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase($"admin-{Guid.NewGuid()}").Options;

    /// <summary>A context over <paramref name="database"/>; <c>EnsureCreated</c> applies the seeded Administrator role.</summary>
    public static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database, TimeProvider? timeProvider = null)
    {
        var dbContext = new RelioDbContext(database, timeProvider ?? TimeProvider.System, FieldProtector);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public static AccountRegistrationService CreateRegistrationService(
        RelioDbContext dbContext,
        RegistrationMode mode,
        TimeProvider? timeProvider = null,
        RegistrationLock? registrationLock = null) =>
        new(
            dbContext,
            UserManagerTestFactory.Create(dbContext),
            registrationLock ?? new RegistrationLock(),
            Options.Create(new RegistrationOptions { Mode = mode }),
            timeProvider ?? TimeProvider.System,
            NullLogger<AccountRegistrationService>.Instance);

    public static UserAdministrationService CreateAdministrationService(
        RelioDbContext dbContext,
        string? currentUserId,
        RegistrationMode mode = RegistrationMode.InviteOnly,
        TimeProvider? timeProvider = null,
        TimeSpan? invitationLifetime = null) =>
        new(
            dbContext,
            UserManagerTestFactory.Create(dbContext),
            new FakeCurrentUser(currentUserId),
            timeProvider ?? TimeProvider.System,
            Options.Create(new RegistrationOptions
            {
                Mode = mode,
                InvitationLifetime = invitationLifetime ?? TimeSpan.FromDays(7),
            }),
            NullLogger<UserAdministrationService>.Instance);

    public static AdministratorBootstrapper CreateBootstrapper(
        RelioDbContext dbContext, string? administratorEmail, TimeProvider? timeProvider = null) =>
        new(
            dbContext,
            UserManagerTestFactory.Create(dbContext),
            Options.Create(new AdministrationOptions { AdministratorEmail = administratorEmail }),
            timeProvider ?? TimeProvider.System,
            NullLogger<AdministratorBootstrapper>.Instance);

    /// <summary>Creates an account directly through Identity (bypassing the registration rules).</summary>
    public static async Task<RelioUser> CreateUserAsync(RelioDbContext dbContext, string email, bool administrator = false)
    {
        var userManager = UserManagerTestFactory.Create(dbContext);
        var user = new RelioUser { UserName = email, Email = email };

        var created = await userManager.CreateAsync(user, StrongPassword);
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));

        if (administrator)
        {
            (await userManager.AddToRoleAsync(user, RelioRoles.Administrator)).Succeeded.Should().BeTrue();
        }

        return user;
    }

    /// <summary>Stores an invitation as the administration service would, returning its plaintext token.</summary>
    public static async Task<string> AddInvitationAsync(
        RelioDbContext dbContext, string email, DateTime expiresAtUtc, string? token = null)
    {
        token ??= InvitationTokens.Generate();
        dbContext.RegistrationInvitations.Add(new RegistrationInvitation
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            TokenHash = InvitationTokens.Hash(token),
            CreatedByUserId = "admin",
            CreatedAtUtc = expiresAtUtc.AddDays(-7),
            ExpiresAtUtc = expiresAtUtc,
        });
        await dbContext.SaveChangesAsync();
        return token;
    }
}
