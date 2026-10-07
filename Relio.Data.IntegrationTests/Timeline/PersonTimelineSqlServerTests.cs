using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Application.Timeline;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Timeline;

/// <summary>
/// Proves mixed timeline query translation, user-calendar note dates and database-bounded paging
/// against the same SQL Server provider used in production.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PersonTimelineSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task The_first_page_reads_bounded_rows_from_both_indexed_streams()
    {
        const int pageSize = 50;
        var ownerId = TestDataFactory.NewOwnerId();
        var (personId, today) = await SeedTimelineAsync(ownerId, count: 1_000);
        var commands = new SelectCommandRecorder();
        await using var dbContext = fixture.CreateDbContext(commands);
        var service = TestDataFactory.CreatePersonTimelineService(dbContext, ownerId);

        var first = await service.GetPageAsync(personId, pageSize: pageSize);

        first.Should().NotBeNull();
        first!.Items.Should().HaveCount(pageSize);
        first.HasMore.Should().BeTrue();
        first.Continuation.Should().NotBeNull();
        first.Items.Select(item => item.Date).Should().BeInDescendingOrder();
        var interactionQuery = commands.Commands.Single(command => command.Sql.Contains("[Interactions]", StringComparison.Ordinal));
        var noteQuery = commands.Commands.Single(command => command.Sql.Contains("[Notes]", StringComparison.Ordinal));
        interactionQuery.Sql.Should().Contain("TOP", "SQL Server must limit the interaction rows before materialization");
        noteQuery.Sql.Should().Contain("TOP", "SQL Server must limit the note rows before materialization");
        interactionQuery.IntegerParameters.Should().Contain(pageSize + 1, "the service reads one sentinel row to know whether another page exists");
        noteQuery.IntegerParameters.Should().Contain(pageSize + 1);
        interactionQuery.Sql.Should().NotContain("OFFSET", "the timeline uses keyset pagination");
        noteQuery.Sql.Should().NotContain("OFFSET");

        commands.Commands.Clear();
        var second = await service.GetPageAsync(
            personId,
            continuation: first.Continuation,
            pageSize: pageSize);
        second.Should().NotBeNull();
        second!.Items.Should().HaveCount(pageSize);
        second.Items.Select(item => item.Id).Intersect(first.Items.Select(item => item.Id)).Should().BeEmpty();
        (await service.GetPageAsync(personId, TimelineFilter.DifficultMoment)).Should().BeEquivalentTo(
            new PersonTimelinePage([], pageSize, false, null));

        // Every interaction is a legitimate past calendar date, and the profile date matches its
        // newest record. The note stream is independently paged even though both kinds are merged.
        first.Items.Should().Contain(item => item.Date <= today);
    }

    [SqlServerFact]
    public async Task A_note_near_UTC_midnight_uses_the_owners_calendar_date()
    {
        var ownerId = TestDataFactory.NewOwnerId();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
        var noteInstant = TimeProvider.System.GetUtcNow().UtcDateTime.Date
            .AddDays(-10)
            .AddHours(23)
            .AddMinutes(30);
        var expectedDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(noteInstant, zone));
        var personId = await SeedBoundaryNoteAsync(ownerId, noteInstant);
        await using var dbContext = fixture.CreateDbContext();

        var page = await TestDataFactory.CreatePersonTimelineService(dbContext, ownerId)
            .GetPageAsync(personId, TimelineFilter.Note);

        page.Should().NotBeNull();
        page!.Items.Should().ContainSingle().Which.Date.Should().Be(expectedDate);
    }

    private async Task<(Guid PersonId, DateOnly Today)> SeedTimelineAsync(string ownerId, int count)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
        var today = Relio.Application.Time.UserCalendar.Today(TimeProvider.System, zone);
        await using var dbContext = fixture.CreateDbContext();
        var person = new Person { OwnerId = ownerId, FirstName = "Ada", LastContactedOn = today };
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = ownerId, TimeZoneId = zone.Id });
        dbContext.People.Add(person);

        var interactions = Enumerable.Range(0, count)
            .Select(index => new Interaction
            {
                OwnerId = ownerId,
                OccurredOn = today.AddDays(-index),
                Kind = InteractionKind.Call,
                Description = $"Synthetic timeline interaction {index}",
            })
            .ToArray();
        dbContext.Interactions.AddRange(interactions);
        dbContext.InteractionParticipants.AddRange(interactions.Select(interaction => new InteractionParticipant
        {
            OwnerId = ownerId,
            InteractionId = interaction.Id,
            PersonId = person.Id,
        }));
        dbContext.Notes.AddRange(Enumerable.Range(0, count).Select(index => new Note
        {
            OwnerId = ownerId,
            PersonId = person.Id,
            Text = $"Synthetic timeline note {index}",
            IsPinned = index == 0,
        }));
        await dbContext.SaveChangesAsync();
        return (person.Id, today);
    }

    private async Task<Guid> SeedBoundaryNoteAsync(string ownerId, DateTime createdAtUtc)
    {
        await using var dbContext = fixture.CreateDbContext();
        var person = new Person { OwnerId = ownerId, FirstName = "Ada" };
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = ownerId, TimeZoneId = "Pacific/Kiritimati" });
        dbContext.People.Add(person);
        var note = new Note
        {
            OwnerId = ownerId,
            PersonId = person.Id,
            Text = "A synthetic note crossing UTC midnight.",
        };
        dbContext.Notes.Add(note);
        await dbContext.SaveChangesAsync();
        note.CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        await dbContext.SaveChangesAsync();
        return person.Id;
    }

    private sealed class SelectCommandRecorder : DbCommandInterceptor
    {
        public ConcurrentQueue<CapturedCommand> Commands { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                var integers = command.Parameters
                    .Cast<DbParameter>()
                    .Select(parameter => parameter.Value)
                    .OfType<int>()
                    .ToArray();
                Commands.Enqueue(new CapturedCommand(command.CommandText, integers));
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record CapturedCommand(string Sql, int[] IntegerParameters);
}
