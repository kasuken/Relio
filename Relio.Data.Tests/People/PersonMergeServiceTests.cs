using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #28 through <see cref="PersonMergeService"/>: merging moves everything the duplicate had to
/// the primary and removes the duplicate, in one save, and refuses (changing nothing) when either
/// person is missing, when the choices are malformed or when the union would break a limit. InMemory
/// provider; <c>PersonMergeSqlServerTests</c> proves the same against SQL Server, including the
/// transaction.
/// </summary>
public class PersonMergeServiceTests
{
    private const string Owner = "owner-1";

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 11, 30, 0, TimeSpan.Zero);

    // ---- Merge results ---------------------------------------------------------------------

    [Fact]
    public async Task MergeAsync_moves_contact_methods_and_tags_and_removes_the_duplicate()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        var outcome = await CreateService(dbContext).MergeAsync(Request(seeded));

        outcome.Should().Be(MergeOutcome.Merged);
        await using var fresh = CreateDbContext(database);
        var primary = await CreatePeopleService(fresh).GetAsync(seeded.JohnId);
        primary!.ContactMethods.Select(c => c.Value).Should().Equal("john@example.com", "+44 7700 900123", "jon@example.com");
        primary.ContactMethods.Select(c => c.SortOrder).Should().Equal(0, 1, 2);
        primary.ContactMethods.Should().OnlyContain(c => c.PersonId == seeded.JohnId && c.OwnerId == Owner);
        primary.Tags.Select(t => t.Name).Should().Equal("Chess", "Climbing", "Sailing");
        (await CreatePeopleService(fresh).GetAsync(seeded.JonId)).Should().BeNull("the duplicate is gone");
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == seeded.JonId)).Should().Be(0);
        (await TagLinksAsync(fresh)).Should().NotContain(link => link.PersonId == seeded.JonId);
    }

    [Fact]
    public async Task MergeAsync_dedupes_contact_methods_and_keeps_the_primarys_row()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        await CreateService(dbContext).MergeAsync(Request(seeded));

        await using var fresh = CreateDbContext(database);
        var emails = await fresh.ContactMethods.AsNoTracking()
            .Where(c => c.OwnerId == Owner && c.NormalizedValue == "john@example.com")
            .ToListAsync();
        var kept = emails.Should().ContainSingle().Subject;
        kept.Id.Should().Be(seeded.JohnEmailId, "the primary's row wins");
        kept.PersonId.Should().Be(seeded.JohnId);
        kept.Value.Should().Be("john@example.com", "the primary's spelling stays");
        kept.Label.Should().Be("Work", "a label the primary's row lacked is taken from the repeated one");
        (await fresh.ContactMethods.AsNoTracking().AnyAsync(c => c.Id == seeded.JonEmailId)).Should().BeFalse("the repeated row is deleted");
    }

    [Fact]
    public async Task MergeAsync_keeps_tag_rows_and_other_peoples_tag_links()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        await CreateService(dbContext).MergeAsync(Request(seeded));

        await using var fresh = CreateDbContext(database);
        (await fresh.Tags.AsNoTracking().Select(t => t.Name).ToListAsync()).Should().BeEquivalentTo("Chess", "Climbing", "Sailing");
        var links = await TagLinksAsync(fresh);
        links.Where(l => l.PersonId == seeded.GraceId).Select(l => l.TagId).Should().Equal(seeded.ChessId);
        links.Where(l => l.PersonId == seeded.JohnId).Should().HaveCount(3, "one link per distinct tag, none repeated");
        links.Should().HaveCount(4);
        (await fresh.People.AsNoTracking().AnyAsync(p => p.Id == seeded.GraceId)).Should().BeTrue();
        (await fresh.ContactMethods.AsNoTracking().AnyAsync(c => c.PersonId == seeded.GraceId)).Should().BeTrue();
    }

    [Fact]
    public async Task MergeAsync_applies_field_choices_and_keep_both()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        var request = Request(seeded, new Dictionary<MergeField, MergeFieldChoice>
        {
            [MergeField.Name] = MergeFieldChoice.Duplicate,
            [MergeField.Nickname] = MergeFieldChoice.Duplicate,
            [MergeField.RelationshipType] = MergeFieldChoice.Duplicate,
            [MergeField.Birthday] = MergeFieldChoice.Duplicate,
            [MergeField.HowWeMet] = MergeFieldChoice.Both,
            [MergeField.Details] = MergeFieldChoice.Duplicate,
        });

        await CreateService(dbContext).MergeAsync(request);

        var merged = await ReadAsync(database, seeded.JohnId);
        (merged.FirstName, merged.LastName).Should().Be(("Jon", "Smythe"));
        merged.Nickname.Should().Be("Jonny");
        merged.RelationshipTypeId.Should().Be(seeded.ColleagueTypeId);
        (merged.BirthdayDay, merged.BirthdayMonth, merged.BirthdayYear).Should().Be((3, 4, 1990));
        merged.HowWeMet.Should().Be("Conference\n\nChess club", "the primary's text comes first");
        merged.Details.Should().Be("Climbs on Tuesdays.");
    }

    [Fact]
    public async Task MergeAsync_without_choices_keeps_the_primarys_values_and_fills_gaps()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        await CreateService(dbContext).MergeAsync(Request(seeded));

        var merged = await ReadAsync(database, seeded.JohnId);
        (merged.FirstName, merged.LastName).Should().Be(("John", "Smith"));
        merged.Nickname.Should().Be("Johnny");
        merged.RelationshipTypeId.Should().Be(seeded.FriendTypeId);
        (merged.BirthdayDay, merged.BirthdayMonth, merged.BirthdayYear).Should().Be((10, 12, null));
        merged.HowWeMet.Should().Be("Conference");
        merged.Details.Should().Be("Climbs on Tuesdays.", "the primary had none, so the duplicate's is kept");
        merged.CreatedAtUtc.Should().Be(Now.UtcDateTime, "the primary keeps its own identity");
    }

    [Fact]
    public async Task MergeAsync_recalculates_last_contacted_from_interactions_not_stale_profile_values()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(
            dbContext,
            john => john.LastContactedOn = new DateOnly(2026, 10, 5),
            jon => jon.LastContactedOn = new DateOnly(2026, 8, 1));
        await AddInteractionAsync(dbContext, seeded.JohnId, new DateOnly(2026, 3, 1));
        await AddInteractionAsync(dbContext, seeded.JonId, new DateOnly(2026, 8, 1));

        await CreateService(dbContext).MergeAsync(Request(seeded));

        (await ReadAsync(database, seeded.JohnId)).LastContactedOn.Should().Be(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task MergeAsync_moves_and_deduplicates_shared_interaction_participants()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        var movedInteraction = await AddInteractionAsync(
            dbContext,
            seeded.JonId,
            new DateOnly(2026, 10, 1),
            seeded.GraceId);
        var sharedInteraction = await AddInteractionAsync(
            dbContext,
            seeded.JohnId,
            new DateOnly(2026, 10, 3),
            seeded.JonId);

        await CreateService(dbContext).MergeAsync(Request(seeded));

        await using var fresh = CreateDbContext(database);
        var movedParticipants = await fresh.InteractionParticipants.AsNoTracking()
            .Where(participant => participant.InteractionId == movedInteraction)
            .Select(participant => participant.PersonId)
            .ToListAsync();
        movedParticipants.Should().BeEquivalentTo(new[] { seeded.JohnId, seeded.GraceId });
        var sharedParticipants = await fresh.InteractionParticipants.AsNoTracking()
            .Where(participant => participant.InteractionId == sharedInteraction)
            .Select(participant => participant.PersonId)
            .ToListAsync();
        sharedParticipants.Should().ContainSingle().Which.Should().Be(seeded.JohnId);
        (await fresh.People.AsNoTracking().SingleAsync(person => person.Id == seeded.JohnId))
            .LastContactedOn.Should().Be(new DateOnly(2026, 10, 3));
    }

    [Fact]
    public async Task MergeAsync_moves_notes_to_the_primary_without_changing_their_text_or_pin_state()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        dbContext.Notes.AddRange(
            new Note
            {
                OwnerId = Owner,
                PersonId = seeded.JohnId,
                Text = "A pinned note on the primary.",
                IsPinned = true,
            },
            new Note
            {
                OwnerId = Owner,
                PersonId = seeded.JonId,
                Text = "An unpinned note on the duplicate.",
            });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await CreateService(dbContext).MergeAsync(Request(seeded));

        await using var fresh = CreateDbContext(database);
        var notes = await fresh.Notes.AsNoTracking().OrderBy(note => note.Text).ToListAsync();
        notes.Should().HaveCount(2);
        notes.Should().OnlyContain(note => note.PersonId == seeded.JohnId);
        notes.Select(note => (note.Text, note.IsPinned)).Should().Equal(
            ("A pinned note on the primary.", true),
            ("An unpinned note on the duplicate.", false));
    }

    [Fact]
    public async Task Merging_an_active_duplicate_into_an_archived_primary_is_active_by_default()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext, john => (john.IsArchived, john.ArchivedAtUtc) = (true, Now.UtcDateTime.AddDays(-10)));

        await CreateService(dbContext).MergeAsync(Request(seeded));

        var merged = await ReadAsync(database, seeded.JohnId);
        merged.IsArchived.Should().BeFalse();
        merged.ArchivedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task MergeAsync_can_keep_the_archived_state_when_chosen()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var archivedAt = Now.UtcDateTime.AddDays(-10);
        var seeded = await SeedAsync(dbContext, adjustJon: jon => (jon.IsArchived, jon.ArchivedAtUtc) = (true, archivedAt));

        await CreateService(dbContext).MergeAsync(Request(seeded, new Dictionary<MergeField, MergeFieldChoice>
        {
            [MergeField.ArchivedState] = MergeFieldChoice.Duplicate,
        }));

        var merged = await ReadAsync(database, seeded.JohnId);
        merged.IsArchived.Should().BeTrue();
        merged.ArchivedAtUtc.Should().Be(archivedAt);
    }

    [Fact]
    public async Task MergeAsync_merges_two_archived_people()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var johnArchivedAt = Now.UtcDateTime.AddDays(-20);
        var seeded = await SeedAsync(
            dbContext,
            john => (john.IsArchived, john.ArchivedAtUtc) = (true, johnArchivedAt),
            jon => (jon.IsArchived, jon.ArchivedAtUtc) = (true, Now.UtcDateTime.AddDays(-5)));

        (await CreateService(dbContext).MergeAsync(Request(seeded))).Should().Be(MergeOutcome.Merged);

        var merged = await ReadAsync(database, seeded.JohnId);
        merged.IsArchived.Should().BeTrue();
        merged.ArchivedAtUtc.Should().Be(johnArchivedAt, "the primary's archive time is kept");
    }

    [Fact]
    public async Task MergeAsync_saves_once()
    {
        var database = NewDatabase();
        await using var seedContext = CreateDbContext(database);
        var seeded = await SeedAsync(seedContext);
        var counter = new CountingSaveChangesInterceptor();
        await using var dbContext = new RelioDbContext(
            new DbContextOptionsBuilder<RelioDbContext>(database).AddInterceptors(counter).Options,
            new FakeTimeProvider(Now));

        await CreateService(dbContext).MergeAsync(Request(seeded));

        counter.Saves.Should().Be(1, "one SaveChanges is one transaction on SQL Server");
    }

    [Fact]
    public async Task MergeAsync_keeps_a_contact_method_moved_to_the_primary_when_the_duplicate_is_removed()
    {
        // The cascade-timing pitfall: without DetectChanges before Remove, EF would delete the moved row
        // together with the duplicate. Every moved row here must survive.
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        await CreateService(dbContext).MergeAsync(Request(seeded));

        await using var fresh = CreateDbContext(database);
        (await fresh.ContactMethods.AsNoTracking().AnyAsync(c => c.Id == seeded.JonPhoneId && c.PersonId == seeded.JohnId))
            .Should().BeTrue();
        (await fresh.ContactMethods.AsNoTracking().AnyAsync(c => c.Id == seeded.JonOtherEmailId && c.PersonId == seeded.JohnId))
            .Should().BeTrue();
    }

    // ---- Not found and validation ----------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MergeAsync_with_a_missing_primary_or_duplicate_returns_NotFound_and_changes_nothing(bool primaryMissing)
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        var request = primaryMissing
            ? new MergePeopleRequest { PrimaryId = Guid.NewGuid(), DuplicateId = seeded.JonId }
            : new MergePeopleRequest { PrimaryId = seeded.JohnId, DuplicateId = Guid.NewGuid() };

        var outcome = await CreateService(dbContext).MergeAsync(request);

        outcome.Should().Be(MergeOutcome.NotFound);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task MergeAsync_rejects_merging_a_person_with_themselves_before_touching_the_database()
    {
        var database = NewDatabase();
        var counter = new CountingSaveChangesInterceptor();
        await using var dbContext = new RelioDbContext(
            new DbContextOptionsBuilder<RelioDbContext>(database).AddInterceptors(counter).Options,
            new FakeTimeProvider(Now));
        var id = Guid.NewGuid();

        await FluentActions.Awaiting(() => CreateService(dbContext).MergeAsync(new MergePeopleRequest { PrimaryId = id, DuplicateId = id }))
            .Should().ThrowAsync<ArgumentException>();

        counter.Saves.Should().Be(0);
    }

    [Fact]
    public async Task MergeAsync_rejects_a_null_request()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        await FluentActions.Awaiting(() => CreateService(dbContext).MergeAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task MergeAsync_rejects_invalid_choices_for_everyone_without_revealing_whether_they_exist()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        var bad = new Dictionary<MergeField, MergeFieldChoice> { [MergeField.Name] = MergeFieldChoice.Both };

        await FluentActions.Awaiting(() => CreateService(dbContext).MergeAsync(Request(seeded, bad)))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => CreateService(dbContext).MergeAsync(
                new MergePeopleRequest { PrimaryId = Guid.NewGuid(), DuplicateId = Guid.NewGuid(), FieldChoices = bad }))
            .Should().ThrowAsync<ArgumentException>();

        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task MergeAsync_over_the_contact_method_limit_throws_and_saves_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        await AddContactMethodsAsync(dbContext, seeded.JohnId, "p", 11, startSortOrder: 10);
        await AddContactMethodsAsync(dbContext, seeded.JonId, "d", 11, startSortOrder: 10);

        var exception = (await FluentActions.Awaiting(() => CreateService(dbContext).MergeAsync(Request(seeded)))
            .Should().ThrowAsync<PersonValidationException>()).Which;

        exception.Errors.Should().Contain(PersonValidationError.TooManyContactMethods);
        exception.Message.Should().NotContain("example.com", "the message lists codes only");
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().CountAsync()).Should().Be(3);
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == seeded.JonId)).Should().Be(14);
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == seeded.JohnId)).Should().Be(12);
    }

    [Fact]
    public async Task MergeAsync_over_the_tag_limit_throws_and_saves_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        await AddTagsAsync(dbContext, seeded.JohnId, "p", 10);
        await AddTagsAsync(dbContext, seeded.JonId, "d", 10);

        var exception = (await FluentActions.Awaiting(() => CreateService(dbContext).MergeAsync(Request(seeded)))
            .Should().ThrowAsync<PersonValidationException>()).Which;

        exception.Errors.Should().Contain(PersonValidationError.TooManyTags);
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().AnyAsync(p => p.Id == seeded.JonId)).Should().BeTrue();
        (await TagLinksAsync(fresh)).Count(l => l.PersonId == seeded.JonId).Should().Be(12);
        (await TagLinksAsync(fresh)).Count(l => l.PersonId == seeded.JohnId).Should().Be(12);
    }

    [Fact]
    public async Task MergeAsync_with_keep_both_too_long_throws_DetailsTooLong_and_saves_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(
            dbContext,
            john => john.Details = new string('a', Person.DetailsMaxLength - 10),
            jon => jon.Details = new string('b', 50));

        await FluentActions.Awaiting(() => CreateService(dbContext).MergeAsync(Request(seeded, new Dictionary<MergeField, MergeFieldChoice>
            {
                [MergeField.Details] = MergeFieldChoice.Both,
            })))
            .Should().ThrowAsync<PersonValidationException>()
            .Where(e => e.Errors.Contains(PersonValidationError.DetailsTooLong));

        (await ReadAsync(database, seeded.JohnId)).Details!.Length.Should().Be(Person.DetailsMaxLength - 10);
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().AnyAsync(p => p.Id == seeded.JonId)).Should().BeTrue();
    }

    // ---- Tracker and authentication --------------------------------------------------------

    [Theory]
    [InlineData("merged")]
    [InlineData("notfound")]
    [InlineData("invalid")]
    public async Task MergeAsync_leaves_nothing_tracked(string scenario)
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        switch (scenario)
        {
            case "merged":
                await service.MergeAsync(Request(seeded));
                break;
            case "notfound":
                await service.MergeAsync(new MergePeopleRequest { PrimaryId = seeded.JohnId, DuplicateId = Guid.NewGuid() });
                break;
            default:
                await AddTagsAsync(dbContext, seeded.JohnId, "p", 10);
                await AddTagsAsync(dbContext, seeded.JonId, "d", 10);
                await FluentActions.Awaiting(() => service.MergeAsync(Request(seeded))).Should().ThrowAsync<PersonValidationException>();
                break;
        }

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task MergeAsync_after_a_failed_save_does_not_reinsert_anything()
    {
        var database = NewDatabase();
        await using var seedContext = CreateDbContext(database);
        var seeded = await SeedAsync(seedContext);
        await using var dbContext = new RelioDbContext(
            new DbContextOptionsBuilder<RelioDbContext>(database).AddInterceptors(new FailOnceSaveChangesInterceptor()).Options,
            new FakeTimeProvider(Now));
        var service = CreateService(dbContext);

        await FluentActions.Awaiting(() => service.MergeAsync(Request(seeded)))
            .Should().ThrowAsync<InvalidOperationException>("the first save fails");
        dbContext.ChangeTracker.Entries().Should().BeEmpty("the tracker is cleared even when the save failed");
        await AssertUnchangedAsync(database, seeded);

        (await service.MergeAsync(Request(seeded))).Should().Be(MergeOutcome.Merged);

        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().CountAsync()).Should().Be(2);
        (await fresh.ContactMethods.AsNoTracking().CountAsync()).Should().Be(4);
        (await TagLinksAsync(fresh)).Should().HaveCount(4);
    }

    [Fact]
    public async Task Every_method_without_an_authenticated_user_throws()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = new PersonMergeService(dbContext, new FakeCurrentUser(null), new FakeTimeProvider(Now));

        await FluentActions.Awaiting(() => service.ListCandidatesAsync(Guid.NewGuid())).Should().ThrowAsync<UnauthenticatedUserException>();
        await FluentActions.Awaiting(() => service.MergeAsync(new MergePeopleRequest { PrimaryId = Guid.NewGuid(), DuplicateId = Guid.NewGuid() }))
            .Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public async Task Reads_through_the_same_context_see_the_merge()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var seeded = await SeedAsync(dbContext);
        var people = CreatePeopleService(dbContext);
        (await people.GetAsync(seeded.JonId)).Should().NotBeNull("the read happens first, as a circuit's would");

        await CreateService(dbContext).MergeAsync(Request(seeded));

        (await people.GetAsync(seeded.JonId)).Should().BeNull();
        (await people.GetAsync(seeded.JohnId))!.ContactMethods.Should().HaveCount(3);
    }

    // ---- Candidates ------------------------------------------------------------------------

    [Fact]
    public async Task ListCandidatesAsync_lists_everyone_else_including_archived_and_never_the_person()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var seeded = await SeedAsync(dbContext);
        await CreatePeopleService(dbContext).ArchiveAsync(seeded.GraceId);

        var candidates = await CreateService(dbContext).ListCandidatesAsync(seeded.JohnId);

        candidates!.Others.Select(p => p.Id).Should().Equal(seeded.GraceId, seeded.JonId);
        candidates.Others.Single(p => p.Id == seeded.GraceId).IsArchived.Should().BeTrue();
        candidates.Others.Should().NotContain(p => p.Id == seeded.JohnId);
        candidates.Suggestions.Should().NotContain(s => s.Id == seeded.JohnId);
    }

    [Fact]
    public async Task ListCandidatesAsync_suggests_possible_duplicates_first()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var seeded = await SeedAsync(dbContext);

        var candidates = await CreateService(dbContext).ListCandidatesAsync(seeded.JohnId);

        var suggestion = candidates!.Suggestions.Should().ContainSingle().Subject;
        suggestion.Id.Should().Be(seeded.JonId);
        suggestion.Reasons.Should().Contain(PossibleDuplicateReason.SameEmail);
        candidates.Suggestions.Select(s => s.Id).Should().NotContain(seeded.GraceId);
    }

    [Fact]
    public async Task ListCandidatesAsync_suggests_a_similar_name()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var seeded = await SeedAsync(dbContext);
        dbContext.ContactMethods.RemoveRange(await dbContext.ContactMethods.Where(c => c.PersonId == seeded.JonId).ToListAsync());
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var jon = await dbContext.People.SingleAsync(p => p.Id == seeded.JonId);
        jon.LastName = "Smith";
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var candidates = await CreateService(dbContext).ListCandidatesAsync(seeded.JohnId);

        candidates!.Suggestions.Should().ContainSingle().Which.Reasons.Should().Contain(PossibleDuplicateReason.SimilarName);
    }

    [Fact]
    public async Task ListCandidatesAsync_for_a_missing_person_returns_null()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        (await CreateService(dbContext).ListCandidatesAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ListCandidatesAsync_leaves_nothing_tracked()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var seeded = await SeedAsync(dbContext);

        await CreateService(dbContext).ListCandidatesAsync(seeded.JohnId);

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static MergePeopleRequest Request(Seeded seeded, IReadOnlyDictionary<MergeField, MergeFieldChoice>? choices = null) =>
        new() { PrimaryId = seeded.JohnId, DuplicateId = seeded.JonId, FieldChoices = choices };

    private static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database) =>
        new(database, new FakeTimeProvider(Now));

    private static PersonMergeService CreateService(RelioDbContext dbContext) =>
        new(dbContext, new FakeCurrentUser(Owner), new FakeTimeProvider(Now));

    private static PeopleService CreatePeopleService(RelioDbContext dbContext) =>
        new(dbContext, new FakeCurrentUser(Owner), new FakeTimeProvider(Now));

    private static async Task<Person> ReadAsync(DbContextOptions<RelioDbContext> database, Guid personId)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.People.AsNoTracking().SingleAsync(p => p.Id == personId);
    }

    /// <summary>Every row of the <c>PersonTags</c> join, read directly so an orphaned link cannot hide.</summary>
    private static async Task<List<(Guid PersonId, Guid TagId)>> TagLinksAsync(RelioDbContext dbContext)
    {
        var links = await dbContext.Set<Dictionary<string, object>>("PersonTag").AsNoTracking().ToListAsync();
        return links.Select(link => ((Guid)link["PeopleId"], (Guid)link["TagsId"])).ToList();
    }

    /// <summary>The state <see cref="SeedAsync"/> left, checked through a brand new context.</summary>
    private static async Task AssertUnchangedAsync(DbContextOptions<RelioDbContext> database, Seeded seeded)
    {
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().CountAsync()).Should().Be(3);
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == seeded.JohnId)).Should().Be(1);
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == seeded.JonId)).Should().Be(3);
        (await fresh.ContactMethods.AsNoTracking().CountAsync()).Should().Be(5);
        var links = await TagLinksAsync(fresh);
        links.Count(l => l.PersonId == seeded.JohnId).Should().Be(2);
        links.Count(l => l.PersonId == seeded.JonId).Should().Be(2);
        links.Should().HaveCount(5);
        var john = await fresh.People.AsNoTracking().SingleAsync(p => p.Id == seeded.JohnId);
        john.Details.Should().BeNull();
        john.Nickname.Should().Be("Johnny");
    }

    private static async Task AddContactMethodsAsync(RelioDbContext dbContext, Guid personId, string prefix, int count, int startSortOrder)
    {
        for (var i = 0; i < count; i++)
        {
            dbContext.ContactMethods.Add(Contact(personId, ContactMethodKind.Email, $"{prefix}{i}@example.com", startSortOrder + i));
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static async Task AddTagsAsync(RelioDbContext dbContext, Guid personId, string prefix, int count)
    {
        var person = await dbContext.People.Include(p => p.Tags).SingleAsync(p => p.Id == personId);
        for (var i = 0; i < count; i++)
        {
            var tag = new Tag { OwnerId = Owner, Name = $"{prefix}{i}" };
            dbContext.Tags.Add(tag);
            person.Tags.Add(tag);
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static async Task<Guid> AddInteractionAsync(
        RelioDbContext dbContext,
        Guid firstPersonId,
        DateOnly occurredOn,
        Guid? secondPersonId = null)
    {
        var interaction = new Interaction
        {
            OwnerId = Owner,
            OccurredOn = occurredOn,
            Kind = InteractionKind.Meeting,
            Description = "A private interaction.",
        };
        dbContext.Interactions.Add(interaction);
        dbContext.InteractionParticipants.Add(new InteractionParticipant
        {
            OwnerId = Owner,
            InteractionId = interaction.Id,
            PersonId = firstPersonId,
        });
        if (secondPersonId is { } second)
        {
            dbContext.InteractionParticipants.Add(new InteractionParticipant
            {
                OwnerId = Owner,
                InteractionId = interaction.Id,
                PersonId = second,
            });
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return interaction.Id;
    }

    private sealed record Seeded(
        Guid JohnId,
        Guid JonId,
        Guid GraceId,
        Guid FriendTypeId,
        Guid ColleagueTypeId,
        Guid ChessId,
        Guid JohnEmailId,
        Guid JonEmailId,
        Guid JonPhoneId,
        Guid JonOtherEmailId);

    /// <summary>
    /// John Smith (the primary: a Friend, nickname Johnny, birthday 10 December, "Conference", the Chess and
    /// Climbing tags, one email without a label) and Jon Smythe (the duplicate: a Colleague, nickname Jonny,
    /// birthday 3 April 1990, "Chess club", details, the Climbing and Sailing tags, the same email as John's
    /// spelled differently and labelled Work, a phone and another email), plus Grace with the Chess tag and
    /// one email. Five contact methods, five tag links, three people. Optional actions adjust John or Jon.
    /// </summary>
    private static async Task<Seeded> SeedAsync(
        RelioDbContext dbContext,
        Action<Person>? adjustJohn = null,
        Action<Person>? adjustJon = null)
    {
        var friend = new RelationshipType { OwnerId = Owner, Name = "Friend" };
        var colleague = new RelationshipType { OwnerId = Owner, Name = "Colleague" };
        var chess = new Tag { OwnerId = Owner, Name = "Chess" };
        var climbing = new Tag { OwnerId = Owner, Name = "Climbing" };
        var sailing = new Tag { OwnerId = Owner, Name = "Sailing" };
        var john = new Person
        {
            OwnerId = Owner,
            FirstName = "John",
            LastName = "Smith",
            Nickname = "Johnny",
            RelationshipTypeId = friend.Id,
            BirthdayDay = 10,
            BirthdayMonth = 12,
            HowWeMet = "Conference",
            Tags = { chess, climbing },
        };
        var jon = new Person
        {
            OwnerId = Owner,
            FirstName = "Jon",
            LastName = "Smythe",
            Nickname = "Jonny",
            RelationshipTypeId = colleague.Id,
            BirthdayDay = 3,
            BirthdayMonth = 4,
            BirthdayYear = 1990,
            HowWeMet = "Chess club",
            Details = "Climbs on Tuesdays.",
            Tags = { climbing, sailing },
        };
        var grace = new Person { OwnerId = Owner, FirstName = "Grace", Tags = { chess } };
        adjustJohn?.Invoke(john);
        adjustJon?.Invoke(jon);

        var johnEmail = Contact(john.Id, ContactMethodKind.Email, "john@example.com", 0);
        var jonEmail = Contact(jon.Id, ContactMethodKind.Email, "JOHN@example.com", 0, "Work");
        var jonPhone = Contact(jon.Id, ContactMethodKind.Phone, "+44 7700 900123", 1);
        var jonOtherEmail = Contact(jon.Id, ContactMethodKind.Email, "jon@example.com", 2);

        dbContext.RelationshipTypes.AddRange(friend, colleague);
        dbContext.Tags.AddRange(chess, climbing, sailing);
        dbContext.People.AddRange(john, jon, grace);
        dbContext.ContactMethods.AddRange(
            johnEmail,
            jonEmail,
            jonPhone,
            jonOtherEmail,
            Contact(grace.Id, ContactMethodKind.Email, "grace@example.com", 0));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return new Seeded(john.Id, jon.Id, grace.Id, friend.Id, colleague.Id, chess.Id, johnEmail.Id, jonEmail.Id, jonPhone.Id, jonOtherEmail.Id);
    }

    private static ContactMethod Contact(Guid personId, ContactMethodKind kind, string value, int sortOrder, string? label = null) =>
        new()
        {
            OwnerId = Owner,
            PersonId = personId,
            Kind = kind,
            Label = label,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(kind, value),
            SortOrder = sortOrder,
        };
}
