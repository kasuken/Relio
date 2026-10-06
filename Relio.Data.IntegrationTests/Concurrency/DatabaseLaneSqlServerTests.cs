using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.People;
using Relio.Application.Profile;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.DependencyInjection;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Profile;
using Relio.Data.Time;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Concurrency;

/// <summary>
/// Proves, against a real SQL Server, that components of one Blazor circuit (or one prerender) can
/// start data service calls at the same moment on their shared scoped <see cref="RelioDbContext"/>
/// without EF Core throwing "A second operation was started on this context instance before a
/// previous operation completed".
/// </summary>
/// <remarks>
/// The EF Core InMemory provider completes every "async" operation synchronously, so it can never
/// show this: only a real provider returns an incomplete task, which is what lets Blazor's renderer
/// move on to the next sibling component while the first one still waits for the database.
/// <see cref="SlowReaderInterceptor"/> makes that overlap deterministic. Each test resolves its
/// services from the real <c>AddRelioData</c> registration, in ONE scope (a scope is a circuit),
/// and starts the calls back to back inside <see cref="Task.WhenAll(Task[])"/> - the same shape as
/// sibling components' <c>OnInitializedAsync</c>.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class DatabaseLaneSqlServerTests(SqlServerDatabaseFixture fixture)
{
    private static readonly TimeSpan QueryDelay = TimeSpan.FromMilliseconds(150);

    [SqlServerFact]
    public async Task Settings_sections_loading_together_do_not_collide()
    {
        var ownerId = await SeedProfileAsync("Europe/Rome", "Ada");
        await using var provider = BuildProvider(ownerId);
        await using var scope = provider.CreateAsyncScope();
        var profile = scope.ServiceProvider.GetRequiredService<IUserProfileService>();
        var timeZone = scope.ServiceProvider.GetRequiredService<IUserTimeZoneService>();
        var twoFactor = scope.ServiceProvider.GetRequiredService<ITwoFactorStatusService>();

        // What /settings does: three sibling components start loading in the same render pass.
        Task<string?>? displayName = null;
        Task<TimeZoneInfo>? zone = null;
        Task<TwoFactorStatus>? status = null;
        var act = async () =>
        {
            displayName = profile.GetDisplayNameAsync();
            zone = timeZone.GetTimeZoneAsync();
            status = twoFactor.GetStatusAsync();
            await Task.WhenAll(displayName, zone, status);
        };

        await act.Should().NotThrowAsync();
        (await displayName!).Should().Be("Ada");
        (await zone!).Id.Should().Be("Europe/Rome");
        (await status!).IsEnabled.Should().BeFalse();
    }

    [SqlServerFact]
    public async Task Administration_sections_loading_together_do_not_collide()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        string adminId;
        await using (var seedProvider = BuildProvider(null))
        await using (var seedScope = seedProvider.CreateAsyncScope())
        {
            var userManager = seedScope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
            var admin = new RelioUser { UserName = email, Email = email };
            (await userManager.CreateAsync(admin)).Succeeded.Should().BeTrue();
            (await userManager.AddToRoleAsync(admin, RelioRoles.Administrator)).Succeeded.Should().BeTrue();
            adminId = admin.Id;
        }

        await using var provider = BuildProvider(adminId);
        await using var scope = provider.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        var timeZone = scope.ServiceProvider.GetRequiredService<IUserTimeZoneService>();

        // What /admin/users does in InviteOnly mode: AccountList and InvitationPanel load together.
        Task<IReadOnlyList<AccountSummary>>? accounts = null;
        var act = async () =>
        {
            accounts = administration.ListAccountsAsync();
            await Task.WhenAll(
                accounts,
                timeZone.GetTimeZoneAsync(),
                timeZone.GetTodayAsync(),
                administration.ListPendingInvitationsAsync());
        };

        await act.Should().NotThrowAsync();
        (await accounts!).Should().Contain(a => a.UserId == adminId && a.IsAdministrator);
    }

    [SqlServerFact]
    public async Task People_pages_loading_and_saving_together_do_not_collide()
    {
        var ownerId = await SeedProfileAsync("Europe/Rome", "Ada");
        await using var provider = BuildProvider(ownerId);
        await using var scope = provider.CreateAsyncScope();
        var types = scope.ServiceProvider.GetRequiredService<IRelationshipTypeService>();
        var people = scope.ServiceProvider.GetRequiredService<IPeopleService>();

        // PersonForm loads relationship types while a Save from an earlier form is still running.
        var act = async () => await Task.WhenAll(
            types.ListAsync(),
            people.ListAsync(),
            people.CreateAsync(new CreatePersonRequest { FirstName = "Grace" }));

        await act.Should().NotThrowAsync();
        (await people.ListAsync()).Should().ContainSingle(p => p.FirstName == "Grace");
    }

    [SqlServerFact]
    public async Task The_person_form_loading_its_pickers_while_a_save_with_new_tags_runs_does_not_collide()
    {
        var ownerId = await SeedProfileAsync("Europe/Rome", "Ada");
        await using var provider = BuildProvider(ownerId);
        await using var scope = provider.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<ITagService>();
        var types = scope.ServiceProvider.GetRequiredService<IRelationshipTypeService>();
        var people = scope.ServiceProvider.GetRequiredService<IPeopleService>();
        var person = await people.CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        // An edit page: PersonForm lists types and tags while a save of the previous form (which
        // creates a tag and replaces the contact methods) is still on its way to the database.
        Task<bool>? update = null;
        var act = async () =>
        {
            update = people.UpdateAsync(person.Id, new UpdatePersonRequest
            {
                FirstName = "Ada",
                NewTagNames = ["Climbing"],
                ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")],
            });
            await Task.WhenAll(update, types.ListAsync(), tags.ListAsync(), people.GetAsync(person.Id));
        };

        await act.Should().NotThrowAsync();
        (await update!).Should().BeTrue();
        (await tags.ListAsync()).Select(t => t.Name).Should().Equal("Climbing");
        var read = await people.GetAsync(person.Id);
        read!.Tags.Should().ContainSingle().Which.Name.Should().Be("Climbing");
        read.ContactMethods.Should().ContainSingle().Which.Value.Should().Be("ada@example.com");
    }

    [SqlServerFact]
    public async Task Two_saves_started_together_both_land()
    {
        var ownerId = await SeedProfileAsync("Europe/Rome", "Ada");
        await using var provider = BuildProvider(ownerId);
        await using var scope = provider.CreateAsyncScope();
        var profile = scope.ServiceProvider.GetRequiredService<IUserProfileService>();
        var timeZone = scope.ServiceProvider.GetRequiredService<IUserTimeZoneService>();

        // The Save buttons of two settings sections, clicked in quick succession.
        var act = async () => await Task.WhenAll(
            profile.SetDisplayNameAsync("Bea"),
            timeZone.SetTimeZoneAsync("America/New_York"));

        await act.Should().NotThrowAsync();
        await using var dbContext = fixture.CreateDbContext();
        var row = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == ownerId);
        row.DisplayName.Should().Be("Bea");
        row.TimeZoneId.Should().Be("America/New_York");
    }

    /// <summary>
    /// The control: the very same reads on one context WITHOUT the lane do collide. This proves the
    /// harness really overlaps operations, so the tests above cannot pass by accident.
    /// </summary>
    [SqlServerFact]
    public async Task Without_the_lane_the_same_reads_collide()
    {
        var ownerId = await SeedProfileAsync("Europe/Rome", "Ada");
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(new SlowReaderInterceptor(QueryDelay))
            .Options;
        await using var dbContext = new RelioDbContext(options, TimeProvider.System);
        var user = new FakeCurrentUser(ownerId);

        var act = async () => await Task.WhenAll(
            new UserProfileService(dbContext, user).GetDisplayNameAsync(),
            new UserTimeZoneService(dbContext, user, TimeProvider.System).GetTimeZoneAsync());

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*second operation*");
    }

    private async Task<string> SeedProfileAsync(string timeZoneId, string displayName)
    {
        var ownerId = TestDataFactory.NewOwnerId();
        await using var dbContext = fixture.CreateDbContext();
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = ownerId,
            TimeZoneId = timeZoneId,
            DisplayName = displayName,
        });
        await dbContext.SaveChangesAsync();
        return ownerId;
    }

    /// <summary>The real production registration, plus the Identity pieces Relio.Web adds.</summary>
    private ServiceProvider BuildProvider(string? userId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ServiceCollectionExtensions.ProviderConfigurationKey] = ServiceCollectionExtensions.SqlServerProvider,
            [$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] = fixture.ConnectionString,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRelioData(configuration);
        // UserAdministrationService needs a UserManager over the same scoped context, as in the app.
        services.AddIdentityCore<RelioUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<RelioDbContext>();
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser(userId));
        // Holds each query open briefly, so the overlap Blazor's renderer creates is deterministic.
        services.ConfigureDbContext<RelioDbContext>(options =>
            options.AddInterceptors(new SlowReaderInterceptor(QueryDelay)));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
