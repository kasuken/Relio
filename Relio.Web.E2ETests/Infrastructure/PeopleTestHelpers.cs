using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>One person for <see cref="PeopleTestHelpers.SeedPeopleAsync"/>.</summary>
/// <param name="FirstName">The first name.</param>
/// <param name="LastName">The last name, if any.</param>
/// <param name="LastContactedOn">The calendar date of the last interaction, or <see langword="null"/> for never.</param>
/// <param name="IsArchived">Whether the person starts archived.</param>
/// <param name="CreatedAtUtc">Backdates the profile's creation time, so "Recently added" has a known order.</param>
public sealed record SeedPerson(
    string FirstName,
    string? LastName = null,
    DateOnly? LastContactedOn = null,
    bool IsArchived = false,
    DateTime? CreatedAtUtc = null);

/// <summary>Seeds people straight into a running app's database, for tests that need many or specific ones.</summary>
public static class PeopleTestHelpers
{
    /// <summary>
    /// Creates one person, with whatever contact methods and tags the request carries, through the
    /// real <c>PeopleService</c> acting as <paramref name="ownerId"/> - the way a signed-in user's
    /// save would, so the rows are exactly what the app itself would have stored.
    /// </summary>
    public static async Task<Guid> CreatePersonAsync(RelioWebAppFactory app, string ownerId, CreatePersonRequest request)
    {
        using var scope = app.CreateRealScope();
        var people = new PeopleService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return (await people.CreateAsync(request)).Id;
    }

    /// <summary>The names of every tag <paramref name="ownerId"/> has, straight from the database, sorted.</summary>
    public static async Task<IReadOnlyList<string>> TagNamesAsync(RelioWebAppFactory app, string ownerId)
    {
        using var scope = app.CreateRealScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        return await dbContext.Tags.AsNoTracking()
            .Where(t => t.OwnerId == ownerId)
            .Select(t => t.Name)
            .OrderBy(name => name)
            .ToListAsync();
    }

    private sealed class OwnerCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }

    /// <summary>
    /// Adds <paramref name="people"/> for <paramref name="ownerId"/> through the app's own
    /// <see cref="RelioDbContext"/>. A person with a <see cref="SeedPerson.CreatedAtUtc"/> is saved
    /// twice: the context stamps <c>CreatedAtUtc</c> when a row is added and only stamps
    /// <c>UpdatedAtUtc</c> when it is modified, so the backdated value is written by the second save.
    /// Everyone is saved in one batch otherwise, so 50+ people stay quick.
    /// </summary>
    public static async Task SeedPeopleAsync(RelioWebAppFactory app, string ownerId, params SeedPerson[] people)
    {
        using var scope = app.CreateRealScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var entities = people.Select(p => new Person
        {
            OwnerId = ownerId,
            FirstName = p.FirstName,
            LastName = p.LastName,
            LastContactedOn = p.LastContactedOn,
            IsArchived = p.IsArchived,
            ArchivedAtUtc = p.IsArchived ? now : null,
        }).ToList();

        dbContext.People.AddRange(entities);
        await dbContext.SaveChangesAsync();

        var backdated = false;
        for (var i = 0; i < people.Length; i++)
        {
            if (people[i].CreatedAtUtc is { } createdAt)
            {
                entities[i].CreatedAtUtc = createdAt;
                backdated = true;
            }
        }

        if (backdated)
        {
            await dbContext.SaveChangesAsync();
        }
    }
}
