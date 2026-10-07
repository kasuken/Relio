using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Notes;
using Relio.Application.Security;
using Relio.Data.Notes;
using Relio.Data.Tests.People;
using Relio.Domain;

namespace Relio.Data.Tests.Notes;

public class NoteServiceTests
{
    private const string Owner = "note-owner";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 7, 35, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_trims_text_sets_owner_and_uses_central_audit_timestamps()
    {
        var database = NewDatabaseName();
        var timeProvider = new FakeTimeProvider(Now);
        Guid personId;
        await using (var setup = CreateDbContext(database, timeProvider))
        {
            personId = await AddPersonAsync(setup, Owner);
        }

        var saves = new CountingSaveChangesInterceptor();
        await using var dbContext = CreateDbContext(database, timeProvider, saves);
        var service = CreateService(dbContext, Owner);

        var created = await service.CreateAsync(new CreateNoteRequest(personId, "  A private note.  ", IsPinned: true));

        created.OwnerId.Should().Be(Owner);
        created.PersonId.Should().Be(personId);
        created.Text.Should().Be("A private note.");
        created.IsPinned.Should().BeTrue();
        created.CreatedAtUtc.Should().Be(Now.UtcDateTime);
        created.UpdatedAtUtc.Should().Be(Now.UtcDateTime);
        saves.Saves.Should().Be(1);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        var stored = await dbContext.Set<Note>().AsNoTracking().SingleAsync();
        stored.Text.Should().Be("A private note.");
    }

    [Fact]
    public async Task GetAsync_and_ListPinnedAsync_return_untracked_snapshots()
    {
        var database = NewDatabaseName();
        await using var dbContext = CreateDbContext(database, TimeProvider.System);
        var personId = await AddPersonAsync(dbContext, Owner);
        var service = CreateService(dbContext, Owner);
        var created = await service.CreateAsync(new CreateNoteRequest(personId, "Keep this close.", IsPinned: true));

        var read = await service.GetAsync(created.Id);
        var pinned = await service.ListPinnedAsync(personId);

        read.Should().NotBeNull();
        dbContext.Entry(read!).State.Should().Be(EntityState.Detached);
        pinned.Should().ContainSingle();
        pinned.Should().OnlyContain(note => dbContext.Entry(note).State == EntityState.Detached);
    }

    [Fact]
    public async Task ListPinnedAsync_includes_archived_people_orders_newest_first_and_keeps_ties_stable()
    {
        var database = NewDatabaseName();
        var timeProvider = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(database, timeProvider);
        var personId = await AddPersonAsync(dbContext, Owner, isArchived: true);
        var service = CreateService(dbContext, Owner);
        var older = await service.CreateAsync(new CreateNoteRequest(personId, "Older pinned note.", IsPinned: true));
        await service.CreateAsync(new CreateNoteRequest(personId, "Not pinned."));
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var newer = await service.CreateAsync(new CreateNoteRequest(personId, "Newer pinned note.", IsPinned: true));
        var sameTime = await service.CreateAsync(new CreateNoteRequest(personId, "Also pinned now.", IsPinned: true));

        var firstRead = await service.ListPinnedAsync(personId);
        var secondRead = await service.ListPinnedAsync(personId);

        firstRead.Select(note => note.Id).Should().HaveCount(3);
        firstRead.Take(2).Select(note => note.Id).Should().BeEquivalentTo(new[] { newer.Id, sameTime.Id });
        firstRead.Last().Id.Should().Be(older.Id);
        secondRead.Select(note => note.Id).Should().Equal(firstRead.Select(note => note.Id));
        firstRead.Select(note => note.Text).Should().Contain("Older pinned note.");
    }

    [Fact]
    public async Task UpdateAsync_replaces_text_and_pin_state_and_stamps_updated_at()
    {
        var database = NewDatabaseName();
        var timeProvider = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(database, timeProvider);
        var personId = await AddPersonAsync(dbContext, Owner);
        var service = CreateService(dbContext, Owner);
        var created = await service.CreateAsync(new CreateNoteRequest(personId, "First version."));
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var updated = await service.UpdateAsync(created.Id, new UpdateNoteRequest("  Revised version.  ", IsPinned: true));
        var stored = await dbContext.Set<Note>().AsNoTracking().SingleAsync(note => note.Id == created.Id);

        updated.Should().BeTrue();
        stored.Text.Should().Be("Revised version.");
        stored.IsPinned.Should().BeTrue();
        stored.CreatedAtUtc.Should().Be(Now.UtcDateTime);
        stored.UpdatedAtUtc.Should().Be(Now.AddMinutes(1).UtcDateTime);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task SetPinnedAsync_is_idempotent_and_delete_removes_only_the_note()
    {
        var database = NewDatabaseName();
        var saves = new CountingSaveChangesInterceptor();
        var timeProvider = new FakeTimeProvider(Now);
        Guid personId;
        await using (var setup = CreateDbContext(database, timeProvider))
        {
            personId = await AddPersonAsync(setup, Owner);
        }

        await using var dbContext = CreateDbContext(database, timeProvider, saves);
        var service = CreateService(dbContext, Owner);
        var note = await service.CreateAsync(new CreateNoteRequest(personId, "A note."));

        (await service.SetPinnedAsync(note.Id, false)).Should().BeTrue();
        saves.Saves.Should().Be(1, "setting the existing state is a no-op");

        (await service.SetPinnedAsync(note.Id, true)).Should().BeTrue();
        saves.Saves.Should().Be(2);
        (await service.DeleteAsync(note.Id)).Should().BeTrue();
        saves.Saves.Should().Be(3);
        (await service.GetAsync(note.Id)).Should().BeNull();
        (await dbContext.People.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Create_and_update_validation_failures_save_nothing_and_clear_the_tracker()
    {
        var database = NewDatabaseName();
        await using var dbContext = CreateDbContext(database, TimeProvider.System);
        var personId = await AddPersonAsync(dbContext, Owner);
        var service = CreateService(dbContext, Owner);

        var createException = await Assert.ThrowsAsync<NoteValidationException>(
            () => service.CreateAsync(new CreateNoteRequest(personId, " \n ")));
        createException.Errors.Should().Equal(NoteValidationError.TextRequired);
        (await dbContext.Set<Note>().CountAsync()).Should().Be(0);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        var created = await service.CreateAsync(new CreateNoteRequest(personId, "Original note."));
        var privateText = new string('x', Note.TextMaxLength + 1);
        var updateException = await Assert.ThrowsAsync<NoteValidationException>(
            () => service.UpdateAsync(created.Id, new UpdateNoteRequest(privateText)));

        updateException.Errors.Should().Equal(NoteValidationError.TextTooLong);
        updateException.Message.Should().NotContain(privateText);
        (await dbContext.Set<Note>().AsNoTracking().SingleAsync()).Text.Should().Be("Original note.");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_save_leaves_no_added_note_tracked_for_the_next_call()
    {
        var database = NewDatabaseName();
        var timeProvider = new FakeTimeProvider(Now);
        Guid personId;
        await using (var setup = CreateDbContext(database, timeProvider))
        {
            personId = await AddPersonAsync(setup, Owner);
        }

        var failOnce = new FailOnceSaveChangesInterceptor();
        await using var dbContext = CreateDbContext(database, timeProvider, failOnce);
        var service = CreateService(dbContext, Owner);

        var failure = () => service.CreateAsync(new CreateNoteRequest(personId, "A private note."));
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("simulated save failure");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.CreateAsync(new CreateNoteRequest(personId, "A second note."));
        (await dbContext.Set<Note>().AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_null_request()
    {
        await using var dbContext = CreateDbContext(NewDatabaseName(), TimeProvider.System);

        var act = () => CreateService(dbContext, Owner).CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static NoteService CreateService(RelioDbContext dbContext, string? userId) =>
        new(dbContext, new FakeCurrentUser(userId));

    private static string NewDatabaseName() => Guid.NewGuid().ToString();

    private static RelioDbContext CreateDbContext(
        string databaseName, TimeProvider timeProvider, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptors)
            .Options;
        return new RelioDbContext(options, timeProvider, FieldProtector);
    }

    private static async Task<Guid> AddPersonAsync(
        RelioDbContext dbContext, string ownerId, bool isArchived = false)
    {
        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = "Ada",
            IsArchived = isArchived,
        };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }
}
