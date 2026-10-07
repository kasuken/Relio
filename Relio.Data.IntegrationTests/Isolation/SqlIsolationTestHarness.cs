using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Administration;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

internal sealed class SqlIsolationTestHarness
{
    public static readonly DateTimeOffset FixedInstant =
        new(2026, 10, 7, 0, 30, 0, TimeSpan.Zero);

    private readonly SqlServerDatabaseFixture _fixture;

    private SqlIsolationTestHarness(
        SqlServerDatabaseFixture fixture,
        SqlIsolationAccount ownerA,
        SqlIsolationAccount ownerB)
    {
        _fixture = fixture;
        OwnerA = ownerA;
        OwnerB = ownerB;
        Clock = new FixedTimeProvider(FixedInstant);
    }

    public const string TestPassword = "Correct-Horse-Battery-9";

    public SqlIsolationAccount OwnerA { get; }

    public SqlIsolationAccount OwnerB { get; }

    public TimeProvider Clock { get; }

    public static async Task<SqlIsolationTestHarness> CreateAsync(
        SqlServerDatabaseFixture fixture,
        bool makeOwnerAAdministrator = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var ownerA = await CreateAccountAsync(fixture, makeOwnerAAdministrator);
        var ownerB = await CreateAccountAsync(fixture, isAdministrator: false);
        return new SqlIsolationTestHarness(fixture, ownerA, ownerB);
    }

    public async Task<SqlIsolationAccount> CreateAdditionalAccountAsync(bool isAdministrator = false) =>
        await CreateAccountAsync(_fixture, isAdministrator);

    public SqlIsolationScope As(string? userId) => new(_fixture, userId, Clock);

    public SqlIsolationScope AsOwnerA() => As(OwnerA.Id);

    public SqlIsolationScope AsOwnerB() => As(OwnerB.Id);

    private static async Task<SqlIsolationAccount> CreateAccountAsync(
        SqlServerDatabaseFixture fixture,
        bool isAdministrator)
    {
        await using var dbContext = fixture.CreateDbContext();
        using var identityServices = CreateIdentityServices(dbContext);
        var userManager = identityServices.GetRequiredService<UserManager<RelioUser>>();
        var email = $"isolation-{Guid.NewGuid():N}@example.com";
        var user = new RelioUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        var createResult = await userManager.CreateAsync(user, TestPassword);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException("A synthetic Identity account could not be created.");
        }

        dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(user.Id));
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = user.Id,
            TimeZoneId = TimeZoneIds.Default,
        });
        await dbContext.SaveChangesAsync();

        if (isAdministrator)
        {
            var roleResult = await userManager.AddToRoleAsync(user, RelioRoles.Administrator);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException("A synthetic account could not be assigned the Administrator role.");
            }
        }

        dbContext.ChangeTracker.Clear();
        return new SqlIsolationAccount(user.Id, email);
    }

    internal static ServiceProvider CreateIdentityServices(RelioDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddLogging();
        DataProtectionTestHarness.ConfigureDataProtection(services);
        services.AddIdentityCore<RelioUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<RelioDbContext>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }
}

internal sealed record SqlIsolationAccount(string Id, string Email);

internal sealed class SqlIsolationScope : IAsyncDisposable
{
    public SqlIsolationScope(
        SqlServerDatabaseFixture fixture,
        string? userId,
        TimeProvider clock)
    {
        DbContext = fixture.CreateDbContext();
        CurrentUser = new FakeCurrentUser(userId);
        Clock = clock;
    }

    public RelioDbContext DbContext { get; }

    public FakeCurrentUser CurrentUser { get; }

    public TimeProvider Clock { get; }

    public TService CreateService<TService>(Func<RelioDbContext, ICurrentUser, TService> factory) =>
        factory(DbContext, CurrentUser);

    public TService CreateService<TService>(
        Func<RelioDbContext, ICurrentUser, TimeProvider, TService> factory) =>
        factory(DbContext, CurrentUser, Clock);

    public ServiceProvider CreateIdentityServices() =>
        SqlIsolationTestHarness.CreateIdentityServices(DbContext);

    public ValueTask DisposeAsync() => DbContext.DisposeAsync();
}

internal sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => instant;
}
