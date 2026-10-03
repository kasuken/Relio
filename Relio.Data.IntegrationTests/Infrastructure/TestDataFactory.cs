using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>Small helpers to seed owner-scoped data and build services for a test.</summary>
internal static class TestDataFactory
{
    /// <summary>
    /// A fresh, random owner id. Every test uses its own owner ids rather than fixed
    /// "user-a"/"user-b" constants, so tests sharing the one per-run database (see
    /// <see cref="SqlServerDatabaseFixture"/>) never see each other's rows - every Relio query and
    /// mutation is already scoped by <c>OwnerId</c>, so distinct owner ids are enough for
    /// isolation without a database per test.
    /// </summary>
    public static string NewOwnerId() => $"owner-{Guid.NewGuid():N}";

    public static PeopleService CreateService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId), TimeProvider.System);

    public static async Task<Guid> CreatePersonAsync(RelioDbContext dbContext, string ownerId, string firstName)
    {
        var person = new Person { OwnerId = ownerId, FirstName = firstName };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        return person.Id;
    }

    public static async Task<Guid> CreateTagAsync(RelioDbContext dbContext, string ownerId, string name)
    {
        var tag = new Tag { OwnerId = ownerId, Name = name };
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();
        return tag.Id;
    }
}
