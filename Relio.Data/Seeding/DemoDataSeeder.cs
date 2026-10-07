using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Application.People;
using Relio.Application.Time;
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
/// Writes <see cref="Person"/>/<see cref="Tag"/>/<see cref="RelationshipType"/>/<see cref="UserProfile"/>
/// rows directly through <see cref="RelioDbContext"/> rather than through
/// <c>IPeopleService</c>/<c>IUserTimeZoneService</c>: those services require a signed-in
/// <c>ICurrentUser</c> (see the "User-scoped data pattern" section of AGENTS.md), which does not
/// exist at startup. Ownership is still enforced the same way every other owned entity enforces
/// it - <see cref="IOwnedEntity.OwnerId"/> is set explicitly on every row, to the demo user's id -
/// this class just plays the role the service layer normally plays, for seed data only. The
/// demo user gets the default relationship types here (issue #22), as every new account does at
/// registration; the sample people use most of them. Some have contact methods (issue #24), using
/// only reserved example domains and fictional numbers; a demo database that was seeded earlier is
/// not backfilled. All the sample text is invented.
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

        // Reuse the demo user's relationship types when they exist (the AddPersonProfile migration
        // adds them for accounts that predate #22), so running twice never duplicates a type.
        var relationshipTypes = await dbContext.RelationshipTypes
            .Where(t => t.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
        if (relationshipTypes.Count == 0)
        {
            relationshipTypes = [.. RelationshipType.CreateDefaults(ownerId)];
            dbContext.RelationshipTypes.AddRange(relationshipTypes);
        }

        RelationshipType? TypeNamed(string name) => relationshipTypes.FirstOrDefault(t => t.Name == name);

        // "Last contacted" is a calendar date in the demo user's own time zone, so it is counted
        // back from today there. Seed-only: issue #34 will derive it from real interactions. The
        // sample people share one CreatedAtUtc (they are saved together), so "Recently added"
        // falls back to the id order for them - fine for a demo.
        var today = UserCalendar.Today(timeProvider, TimeZoneIds.Parse(DemoTimeZoneId));
        DateOnly DaysAgo(int days) => today.AddDays(-days);

        Tag MakeTag(string name) => new() { OwnerId = ownerId, Name = name };

        // Only reserved example domains (RFC 2606) and fictional numbers (+44 7700 900xxx and
        // +1 202 555 01xx are set aside for drama and examples), so no real person is ever
        // addressed. The comparison key comes from the same rule the service uses.
        ContactMethod Contact(ContactMethodKind kind, string value, string? label, int sortOrder) => new()
        {
            OwnerId = ownerId,
            Kind = kind,
            Label = label,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(kind, value),
            SortOrder = sortOrder,
        };

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
                RelationshipType = TypeNamed("Acquaintance"),
                BirthdayYear = 1815,
                BirthdayMonth = 12,
                BirthdayDay = 10,
                HowWeMet = "At a talk about early computing, where she asked the question nobody else had thought of.",
                Details = "Writes long, thoughtful letters.\nInterested in mathematics and music.\nPrefers a quiet table at the back.",
                LastContactedOn = DaysAgo(3),
                Tags = { mentor, friend },
                ContactMethods =
                {
                    Contact(ContactMethodKind.Email, "ada@example.com", "Personal", 0),
                    Contact(ContactMethodKind.Address, "12 Example Square\nLondon", "Home", 1),
                },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Grace",
                LastName = "Hopper",
                BirthdayYear = 1906,
                BirthdayMonth = 12,
                BirthdayDay = 9,
                Tags = { mentor },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Alan",
                LastName = "Turing",
                RelationshipType = TypeNamed("Friend"),
                BirthdayYear = 1912,
                BirthdayMonth = 6,
                BirthdayDay = 23,
                LastContactedOn = DaysAgo(45),
                Tags = { friend },
                ContactMethods = { Contact(ContactMethodKind.Phone, "+1 202 555 0142", "Home", 0) },
            },
            new()
            {
                // Feb 29 birthday - exercises UserCalendar.NextOccurrence's non-leap-year rule
                // (observes Feb 28), see AGENTS.md "Dates and time zones".
                OwnerId = ownerId,
                FirstName = "Marco",
                LastName = "Rossi",
                Nickname = "Marchino",
                RelationshipType = TypeNamed("Family"),
                BirthdayYear = 1992,
                BirthdayMonth = 2,
                BirthdayDay = 29,
                LastContactedOn = today,
                Tags = { family },
                ContactMethods =
                {
                    Contact(ContactMethodKind.Phone, "+44 7700 900123", "Mobile", 0),
                    Contact(ContactMethodKind.Email, "marco.rossi@example.com", null, 1),
                },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Elena",
                LastName = "Conti",
                RelationshipType = TypeNamed("Colleague"),
                HowWeMet = "Her first week on the team; we shared a desk by the window.",
                Details = "Leads the design reviews.\nAllergic to cats.\nAsk about the allotment she is building.",
                LastContactedOn = DaysAgo(12),
                Tags = { work },
                ContactMethods =
                {
                    Contact(ContactMethodKind.Email, "elena.conti@example.org", "Work", 0),
                    Contact(ContactMethodKind.Phone, "+44 7700 900456", "Work", 1),
                    Contact(ContactMethodKind.Social, "@elena.designs", "Instagram", 2),
                },
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Sam",
                LastName = "Okafor",
                RelationshipType = TypeNamed("Friend"),
                BirthdayYear = 1988,
                BirthdayMonth = 7,
                BirthdayDay = 4,
                LastContactedOn = DaysAgo(200),
                Tags = { friend },
                IsArchived = true,
                ArchivedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            },
            new()
            {
                OwnerId = ownerId,
                FirstName = "Priya",
                LastName = "Nair",
                RelationshipType = TypeNamed("Family"),
                BirthdayYear = 1995,
                BirthdayMonth = 11,
                BirthdayDay = 2,
                LastContactedOn = DaysAgo(1),
                Tags = { family },
                ContactMethods = { Contact(ContactMethodKind.Email, "priya@example.net", null, 0) },
            },
            new()
            {
                // A birthday without a year: the day and month are known, the age is not.
                OwnerId = ownerId,
                FirstName = "Liam",
                LastName = "Chen",
                RelationshipType = TypeNamed("Colleague"),
                BirthdayMonth = 3,
                BirthdayDay = 14,
                Tags = { work },
            },
        };

        dbContext.People.AddRange(people);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
