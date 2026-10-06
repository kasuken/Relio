using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Seeding;

/// <summary>
/// Creates a demo account (an Administrator, issue #19) with realistic sample data, so Relio can
/// be explored without manually registering and populating an account first. Runs once at startup (see
/// <c>Relio.Web.Program</c>), never from a request, and is safe to call repeatedly - see
/// <see cref="SeedAsync"/>.
/// </summary>
/// <remarks>
/// Writes <see cref="Person"/>/<see cref="Tag"/>/<see cref="UserProfile"/> rows directly through
/// <see cref="RelioDbContext"/> rather than through <c>IPeopleService</c>/<c>IUserTimeZoneService</c>:
/// those services require a signed-in <c>ICurrentUser</c> (see the "User-scoped data pattern"
/// section of AGENTS.md), which does not exist at startup. Ownership is still enforced the same
/// way every other owned entity enforces it - <see cref="IOwnedEntity.OwnerId"/> is set explicitly
/// on every row, to the demo user's id - this class just plays the role the service layer
/// normally plays, for seed data only.
/// </remarks>
public sealed class DemoDataSeeder(
    RelioDbContext dbContext,
    UserManager<RelioUser> userManager,
    TimeProvider timeProvider,
    IOptions<DemoDataOptions> options,
    IHostEnvironment environment,
    ILogger<DemoDataSeeder> logger)
{
    /// <summary>The demo account's sign-in email.</summary>
    public const string DemoEmail = "demo@relio.local";

    /// <summary>
    /// The demo account's password. Test/demo only - never use this for a real account. Meets
    /// Relio's password policy (see <c>Relio.Web.Identity.ServiceCollectionExtensions</c>) so the
    /// account can always sign in through the normal login page.
    /// </summary>
    public const string DemoPassword = "Relio-Demo#2026";

    /// <summary>The demo profile's time zone (epic #12) - picked to be away from UTC.</summary>
    public const string DemoTimeZoneId = "Europe/Rome";

    /// <summary>
    /// Seeds the demo user and sample data when <c>DemoData:Enabled</c> is <see langword="true"/>
    /// and the current environment is not Production (refusing, with a logged error, otherwise -
    /// demo data must never reach a hosted or self-hosted production deployment). Idempotent: safe
    /// to call on every startup - it does nothing once the demo user and its data already exist.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            logger.LogDebug("Demo data seeding is disabled (DemoData:Enabled is not set to true).");
            return;
        }

        if (environment.IsProduction())
        {
            logger.LogError(
                "Refusing to seed demo data: DemoData:Enabled=true but the environment is " +
                "Production. Demo data is a development/demo convenience and must never run in a " +
                "hosted or self-hosted production deployment.");
            return;
        }

        var demoUser = await userManager.FindByEmailAsync(DemoEmail);
        if (demoUser is null)
        {
            demoUser = new RelioUser
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                EmailConfirmed = true,
            };

            var createResult = await userManager.CreateAsync(demoUser, DemoPassword);
            if (!createResult.Succeeded)
            {
                logger.LogError(
                    "Failed to create the demo user: {Errors}",
                    string.Join("; ", createResult.Errors.Select(e => e.Code)));
                return;
            }

            logger.LogInformation("Created the demo user.");
        }

        // The demo account is the instance's Administrator (issue #19), so the demo shows the
        // administration page. Checked on every run, not only when the user is created, so a demo
        // database that predates #19 gets the role too. Accounts registered while demo data is on
        // are not Administrators: the demo account already exists, so none of them is "first".
        if (!await userManager.IsInRoleAsync(demoUser, RelioRoles.Administrator))
        {
            var roleResult = await userManager.AddToRoleAsync(demoUser, RelioRoles.Administrator);
            if (!roleResult.Succeeded)
            {
                logger.LogError(
                    "Failed to make the demo user an Administrator: {Errors}",
                    string.Join("; ", roleResult.Errors.Select(e => e.Code)));
            }
        }

        var alreadySeeded = await dbContext.People.AnyAsync(p => p.OwnerId == demoUser.Id, cancellationToken);
        if (alreadySeeded)
        {
            logger.LogDebug("Demo sample data already exists; skipping.");
            return;
        }

        await SeedSampleDataAsync(demoUser.Id, cancellationToken);
        logger.LogInformation("Seeded demo sample data.");
    }

    private async Task SeedSampleDataAsync(string ownerId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.FirstOrDefaultAsync(p => p.OwnerId == ownerId, cancellationToken);
        if (profile is null)
        {
            dbContext.UserProfiles.Add(new UserProfile { OwnerId = ownerId, TimeZoneId = DemoTimeZoneId });
        }

        Tag MakeTag(string name) => new() { OwnerId = ownerId, Name = name };

        var family = MakeTag("Family");
        var friend = MakeTag("Friend");
        var work = MakeTag("Work");
        var mentor = MakeTag("Mentor");

        var people = new List<Person>
        {
            new()
            {
                OwnerId = ownerId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Birthday = new DateOnly(1815, 12, 10),
                Tags = { mentor },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Grace",
                LastName = "Hopper",
                Birthday = new DateOnly(1906, 12, 9),
                Tags = { mentor },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Alan",
                LastName = "Turing",
                Birthday = new DateOnly(1912, 6, 23),
                Tags = { friend },
            },
            new()
            {
                // Feb 29 birthday - exercises UserCalendar.NextOccurrence's non-leap-year rule
                // (observes Feb 28), see AGENTS.md "Dates and time zones".
                OwnerId = ownerId,
                FirstName = "Marco",
                LastName = "Rossi",
                Birthday = new DateOnly(1992, 2, 29),
                Tags = { family },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Elena",
                LastName = "Conti",
                Birthday = null,
                Tags = { work },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Sam",
                LastName = "Okafor",
                Birthday = new DateOnly(1988, 7, 4),
                Tags = { friend },
                IsArchived = true,
                ArchivedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Priya",
                LastName = "Nair",
                Birthday = new DateOnly(1995, 11, 2),
                Tags = { family },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Liam",
                LastName = "Chen",
                Birthday = null,
                Tags = { work },
            },
        };

        dbContext.People.AddRange(people);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
