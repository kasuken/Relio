using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.DifficultMoments;
using Relio.Application.Interactions;
using Relio.Application.Notes;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data;
using Relio.Data.DifficultMoments;
using Relio.Data.Interactions;
using Relio.Data.Notes;
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

    /// <summary>Archives a person through the real <c>PeopleService</c> acting as <paramref name="ownerId"/>.</summary>
    public static async Task ArchivePersonAsync(RelioWebAppFactory app, string ownerId, Guid personId)
    {
        using var scope = app.CreateRealScope();
        var people = new PeopleService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        (await people.ArchiveAsync(personId)).Should().BeTrue();
    }

    /// <summary>The id of <paramref name="ownerId"/>'s relationship type called <paramref name="name"/>, straight from the database.</summary>
    public static async Task<Guid> RelationshipTypeIdAsync(RelioWebAppFactory app, string ownerId, string name)
    {
        using var scope = app.CreateRealScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        return await dbContext.RelationshipTypes.AsNoTracking()
            .Where(t => t.OwnerId == ownerId && t.Name == name)
            .Select(t => t.Id)
            .SingleAsync();
    }

    /// <summary>Adds a relationship type for <paramref name="ownerId"/> through the real service.</summary>
    public static async Task CreateRelationshipTypeAsync(RelioWebAppFactory app, string ownerId, string name)
    {
        using var scope = app.CreateRealScope();
        var types = new RelationshipTypeService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(), new OwnerCurrentUser(ownerId));
        await types.CreateAsync(name);
    }

    /// <summary>Every person <paramref name="ownerId"/> has, archived included, through the real <c>PeopleService</c>.</summary>
    public static async Task<IReadOnlyList<Person>> ListPeopleAsync(RelioWebAppFactory app, string ownerId)
    {
        using var scope = app.CreateRealScope();
        var people = new PeopleService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return await people.ListAsync(includeArchived: true);
    }

    /// <summary>
    /// What <c>PeopleService.FindPossibleDuplicatesAsync</c> reports for <paramref name="query"/>,
    /// asked as <paramref name="ownerId"/>.
    /// </summary>
    public static async Task<IReadOnlyList<PossibleDuplicate>> FindPossibleDuplicatesAsync(
        RelioWebAppFactory app, string ownerId, PossibleDuplicateQuery query)
    {
        using var scope = app.CreateRealScope();
        var people = new PeopleService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return await people.FindPossibleDuplicatesAsync(query);
    }

    /// <summary>One person with tags, contact methods and relationship type loaded, through the real <c>PeopleService</c> acting as <paramref name="ownerId"/>.</summary>
    public static async Task<Person?> GetPersonAsync(RelioWebAppFactory app, string ownerId, Guid personId)
    {
        using var scope = app.CreateRealScope();
        var people = new PeopleService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return await people.GetAsync(personId);
    }

    /// <summary>Merges through the real <c>PersonMergeService</c> acting as <paramref name="ownerId"/> (issue #28).</summary>
    public static async Task<MergeOutcome> MergeAsync(RelioWebAppFactory app, string ownerId, MergePeopleRequest request)
    {
        using var scope = app.CreateRealScope();
        var merge = new PersonMergeService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return await merge.MergeAsync(request);
    }

    /// <summary>Creates a shared interaction through the real service acting as <paramref name="ownerId"/>.</summary>
    public static async Task<Guid> CreateInteractionAsync(
        RelioWebAppFactory app,
        string ownerId,
        CreateInteractionRequest request)
    {
        using var scope = app.CreateRealScope();
        var interactions = new InteractionService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return await interactions.CreateAsync(request);
    }

    /// <summary>Creates a note through the real <c>NoteService</c> acting as <paramref name="ownerId"/>.</summary>
    public static async Task<Guid> CreateNoteAsync(
        RelioWebAppFactory app,
        string ownerId,
        Guid personId,
        string text,
        bool isPinned = false)
    {
        using var scope = app.CreateRealScope();
        var notes = new NoteService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId));
        var note = await notes.CreateAsync(new CreateNoteRequest(personId, text, isPinned));
        return note.Id;
    }

    /// <summary>Creates a difficult moment through the real <c>DifficultMomentService</c> acting as <paramref name="ownerId"/>.</summary>
    public static async Task<Guid> CreateDifficultMomentAsync(
        RelioWebAppFactory app,
        string ownerId,
        Guid personId,
        string description,
        DateOnly occurredOn)
    {
        using var scope = app.CreateRealScope();
        var service = new DifficultMomentService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        var moment = await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personId,
            OccurredOn = occurredOn,
            Description = description,
        });
        return moment.Id;
    }

    /// <summary>Seeds a reminder for <paramref name="personId"/> owned by <paramref name="ownerId"/>.</summary>
    public static async Task<Guid> CreateReminderAsync(
        RelioWebAppFactory app,
        string ownerId,
        Guid personId,
        string title,
        DateOnly dueDate)
    {
        using var scope = app.CreateRealScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        var reminder = new Reminder
        {
            OwnerId = ownerId,
            PersonId = personId,
            Title = title,
            DueDate = dueDate,
        };
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();
        return reminder.Id;
    }

    /// <summary>Seeds many interactions in one database save for timeline pagination tests.</summary>
    public static async Task SeedInteractionsAsync(
        RelioWebAppFactory app,
        string ownerId,
        Guid personId,
        DateOnly newestDate,
        int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        using var scope = app.CreateRealScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
        var person = await dbContext.People.SingleAsync(
            profile => profile.Id == personId && profile.OwnerId == ownerId);

        var interactions = Enumerable.Range(0, count)
            .Select(index => new Interaction
            {
                OwnerId = ownerId,
                OccurredOn = newestDate.AddDays(-index),
                Kind = InteractionKind.Call,
                Description = $"Pagination entry {index + 1}",
            })
            .ToArray();
        dbContext.Interactions.AddRange(interactions);
        dbContext.InteractionParticipants.AddRange(interactions.Select(interaction => new InteractionParticipant
        {
            OwnerId = ownerId,
            InteractionId = interaction.Id,
            PersonId = personId,
        }));

        // Keep the denormalized date aligned with the seeded interaction records.
        person.LastContactedOn = newestDate;
        await dbContext.SaveChangesAsync();
    }

    /// <summary>The merge candidates for a person, through the real <c>PersonMergeService</c> acting as <paramref name="ownerId"/>.</summary>
    public static async Task<MergeCandidates?> ListMergeCandidatesAsync(RelioWebAppFactory app, string ownerId, Guid personId)
    {
        using var scope = app.CreateRealScope();
        var merge = new PersonMergeService(
            scope.ServiceProvider.GetRequiredService<RelioDbContext>(),
            new OwnerCurrentUser(ownerId),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return await merge.ListCandidatesAsync(personId);
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
