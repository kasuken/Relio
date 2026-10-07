using Microsoft.EntityFrameworkCore;
using Relio.Application.Interactions;
using Relio.Application.Notes;
using Relio.Application.Security;
using Relio.Application.Timeline;
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
    /// A fresh, random owner id. Every test uses its own owner ids rather than fixed
    /// "user-a"/"user-b" constants, so tests sharing the one per-run database (see
    /// <see cref="SqlServerDatabaseFixture"/>) never see each other's rows - every Relio query and
    /// mutation is already scoped by <c>OwnerId</c>, so distinct owner ids are enough for
    /// isolation without a database per test.
    /// </summary>
    public static string NewOwnerId() => $"owner-{Guid.NewGuid():N}";

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
