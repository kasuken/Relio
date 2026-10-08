using Microsoft.EntityFrameworkCore;
using Relio.Application.Interactions;
using Relio.Application.Notes;
using Relio.Application.Security;
using Relio.Application.Timeline;
using Relio.Data.Identity;
using Relio.Data.Interactions;
using Relio.Data.Notes;
using Relio.Data.People;
using Relio.Data.Timeline;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>Small helpers to seed owner-scoped data and build services for a test.</summary>
internal static class TestDataFactory
{
    /// <summary>
    /// Generates a fresh owner id without creating a user. Tests that persist owner-scoped rows
    /// in SQL Server must also create the corresponding Identity user; other test scopes can use
    /// this pure generator when they seed users themselves.
    /// </summary>
    /// <returns>A fresh owner id string.</returns>
    public static string NewOwnerId() => $"owner-{Guid.NewGuid():N}";

    /// <summary>
    /// Creates a synthetic Identity owner for SQL Server test data. The user and its default
    /// relationship types are saved together, then the defaults are removed in a second setup save
    /// so each test keeps control over the types it seeds.
    /// </summary>
    /// <param name="fixture">The shared SQL Server integration-test fixture.</param>
    /// <param name="ownerId">An optional exact identity id for tests with a required key length.</param>
    /// <returns>The id of the persisted synthetic user.</returns>
    public static async Task<string> CreateOwnerAsync(SqlServerDatabaseFixture fixture, string? ownerId = null)
    {
        var userId = ownerId ?? NewOwnerId();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var normalizedEmail = email.ToUpperInvariant();
        var user = new RelioUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = normalizedEmail,
            Email = email,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        };
        var defaultTypes = RelationshipType.CreateDefaults(user.Id);

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(user);
        dbContext.RelationshipTypes.AddRange(defaultTypes);
        await dbContext.SaveChangesAsync();

        dbContext.RelationshipTypes.RemoveRange(defaultTypes);
        await dbContext.SaveChangesAsync();

        return user.Id;
    }

    public static PeopleService CreateService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId), TimeProvider.System);

    public static PeopleImportService CreatePeopleImportService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId), TimeProvider.System);

    public static PersonMergeService CreatePersonMergeService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId), TimeProvider.System);

    public static InteractionService CreateInteractionService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId), TimeProvider.System);

    public static NoteService CreateNoteService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId));

    public static Task<Note> CreateNoteAsync(
        RelioDbContext dbContext, string ownerId, Guid personId, string text, bool isPinned = false) =>
        CreateNoteService(dbContext, ownerId).CreateAsync(new CreateNoteRequest(personId, text, isPinned));

    public static PersonTimelineService CreatePersonTimelineService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId));

    public static RelationshipTypeService CreateRelationshipTypeService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId));

    public static TagService CreateTagService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId));

    public static Relio.Data.DifficultMoments.DifficultMomentService CreateDifficultMomentService(RelioDbContext dbContext, string? ownerId, TimeProvider? timeProvider = null) =>
        new(dbContext, new FakeCurrentUser(ownerId), timeProvider ?? TimeProvider.System);

    /// <summary>
    /// Creates a person. <paramref name="createdAtUtc"/> backdates the profile: the context stamps
    /// <c>CreatedAtUtc</c> on insert and ignores a value set before it, so the date is assigned
    /// after the first save and saved again (a modified entity only gets its <c>UpdatedAtUtc</c>
    /// stamped). Used to give "Recently added" tests distinct, known creation times.
    /// </summary>
    public static async Task<Guid> CreatePersonAsync(
        RelioDbContext dbContext,
        string ownerId,
        string firstName,
        string? lastName = null,
        DateOnly? lastContactedOn = null,
        bool isArchived = false,
        DateTime? createdAtUtc = null,
        Guid? relationshipTypeId = null,
        IReadOnlyList<Guid>? tagIds = null)
    {
        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = firstName,
            LastName = lastName,
            LastContactedOn = lastContactedOn,
            IsArchived = isArchived,
            ArchivedAtUtc = isArchived ? TimeProvider.System.GetUtcNow().UtcDateTime : null,
            RelationshipTypeId = relationshipTypeId,
        };
        if (tagIds is { Count: > 0 })
        {
            foreach (var tag in await dbContext.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync())
            {
                person.Tags.Add(tag);
            }
        }

        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();

        if (createdAtUtc is { } createdAt)
        {
            person.CreatedAtUtc = createdAt;
            await dbContext.SaveChangesAsync();
        }

        return person.Id;
    }

    public static async Task<Guid> CreateTagAsync(RelioDbContext dbContext, string ownerId, string name)
    {
        var tag = new Tag { OwnerId = ownerId, Name = name };
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();
        return tag.Id;
    }

    /// <summary>Links an existing tag to an existing person (one row in the <c>PersonTags</c> join).</summary>
    public static async Task TagPersonAsync(RelioDbContext dbContext, Guid personId, Guid tagId)
    {
        var person = await dbContext.People.Include(p => p.Tags).SingleAsync(p => p.Id == personId);
        person.Tags.Add(await dbContext.Tags.SingleAsync(t => t.Id == tagId));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    /// <summary>Creates a contact method for <paramref name="personId"/>, with the comparison key the rules compute.</summary>
    public static async Task<Guid> CreateContactMethodAsync(
        RelioDbContext dbContext,
        string ownerId,
        Guid personId,
        string value,
        ContactMethodKind kind = ContactMethodKind.Email,
        int sortOrder = 0,
        string? label = null)
    {
        var contactMethod = new ContactMethod
        {
            OwnerId = ownerId,
            PersonId = personId,
            Kind = kind,
            Label = label,
            Value = value,
            NormalizedValue = Relio.Application.People.ContactMethodRules.ToNormalizedValue(kind, value),
            SortOrder = sortOrder,
        };
        dbContext.ContactMethods.Add(contactMethod);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return contactMethod.Id;
    }

    public static async Task<Guid> CreateRelationshipTypeAsync(
        RelioDbContext dbContext, string ownerId, string name, int sortOrder = 0)
    {
        var type = new RelationshipType { OwnerId = ownerId, Name = name, SortOrder = sortOrder };
        dbContext.RelationshipTypes.Add(type);
        await dbContext.SaveChangesAsync();
        return type.Id;
    }
}
