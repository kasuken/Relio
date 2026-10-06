using Microsoft.Extensions.DependencyInjection;
using Relio.Data;
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
