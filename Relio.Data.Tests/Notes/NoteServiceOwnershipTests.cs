using Microsoft.EntityFrameworkCore;
using Relio.Application.Notes;
using Relio.Application.Ownership;
using Relio.Application.Security;
using Relio.Data.Notes;
using Relio.Data.Tests.People;
using Relio.Domain;

namespace Relio.Data.Tests.Notes;

public class NoteServiceOwnershipTests
{
    private const string UserA = "note-user-a";
    private const string UserB = "note-user-b";

    [Fact]
    public async Task Reads_never_return_another_users_note_or_pinned_notes()
    {
        await using var dbContext = CreateDbContext();
        var personA = await AddPersonAsync(dbContext, UserA);
        var personB = await AddPersonAsync(dbContext, UserB);
        var noteA = await CreateService(dbContext, UserA)
            .CreateAsync(new CreateNoteRequest(personA, "User A note.", IsPinned: true));
        var noteB = await CreateService(dbContext, UserB)
            .CreateAsync(new CreateNoteRequest(personB, "User B note.", IsPinned: true));

        var serviceForB = CreateService(dbContext, UserB);

        (await serviceForB.GetAsync(noteA.Id)).Should().BeNull();
        (await serviceForB.GetAsync(Guid.NewGuid())).Should().BeNull();
        (await serviceForB.ListPinnedAsync(personA)).Should().BeEmpty();
        (await serviceForB.ListPinnedAsync(personB)).Select(note => note.Id).Should().Equal(noteB.Id);
        (await CreateService(dbContext, UserA).ListPinnedAsync(personA)).Select(note => note.Id).Should().Equal(noteA.Id);
    }

    [Fact]
    public async Task Foreign_note_mutations_return_false_and_leave_it_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var personA = await AddPersonAsync(dbContext, UserA);
        var noteA = await CreateService(dbContext, UserA)
            .CreateAsync(new CreateNoteRequest(personA, "Do not change this.", IsPinned: true));
        var serviceForB = CreateService(dbContext, UserB);

        (await serviceForB.UpdateAsync(noteA.Id, new UpdateNoteRequest("Private replacement."))).Should().BeFalse();
        (await serviceForB.SetPinnedAsync(noteA.Id, false)).Should().BeFalse();
        (await serviceForB.DeleteAsync(noteA.Id)).Should().BeFalse();

        var stored = await dbContext.Set<Note>().AsNoTracking().SingleAsync(note => note.Id == noteA.Id);
        stored.OwnerId.Should().Be(UserA);
        stored.Text.Should().Be("Do not change this.");
        stored.IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_rejects_missing_or_foreign_people_without_saving_content()
    {
        await using var dbContext = CreateDbContext();
        var personA = await AddPersonAsync(dbContext, UserA);
        var serviceForB = CreateService(dbContext, UserB);
        const string privateText = "A private relationship detail.";

        var foreign = await Assert.ThrowsAsync<ForeignEntityNotOwnedException>(
            () => serviceForB.CreateAsync(new CreateNoteRequest(personA, privateText)));
        var missing = await Assert.ThrowsAsync<ForeignEntityNotOwnedException>(
            () => serviceForB.CreateAsync(new CreateNoteRequest(Guid.NewGuid(), privateText)));

        foreign.EntityName.Should().Be("people");
        missing.EntityName.Should().Be("people");
        foreign.Message.Should().Be(missing.Message);
        foreign.Message.Should().NotContain(privateText);
        foreign.Message.Should().NotContain(personA.ToString());
        (await dbContext.Set<Note>().CountAsync()).Should().Be(0);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_primary_note_reads_and_mutations_match_foreign_note_results()
    {
        await using var dbContext = CreateDbContext();
        var foreignPersonId = await AddPersonAsync(dbContext, UserA);
        var foreignNote = await CreateService(dbContext, UserA)
            .CreateAsync(new CreateNoteRequest(foreignPersonId, "Foreign note.", IsPinned: true));
        var missingNoteId = Guid.NewGuid();
        var serviceForB = CreateService(dbContext, UserB);

        (await serviceForB.GetAsync(missingNoteId)).Should().BeNull();
        (await serviceForB.UpdateAsync(missingNoteId, new UpdateNoteRequest("Replacement."))).Should().BeFalse();
        (await serviceForB.DeleteAsync(missingNoteId)).Should().BeFalse();
        (await serviceForB.SetPinnedAsync(missingNoteId, false)).Should().BeFalse();
        (await serviceForB.GetAsync(foreignNote.Id)).Should().BeNull();
        (await serviceForB.UpdateAsync(foreignNote.Id, new UpdateNoteRequest("Replacement."))).Should().BeFalse();
        (await serviceForB.DeleteAsync(foreignNote.Id)).Should().BeFalse();
        (await serviceForB.SetPinnedAsync(foreignNote.Id, false)).Should().BeFalse();
    }

    [Fact]
    public async Task Every_service_method_requires_an_authenticated_user()
    {
        await using var dbContext = CreateDbContext();
        var personId = await AddPersonAsync(dbContext, UserA);
        var service = CreateService(dbContext, null);
        var noteId = Guid.NewGuid();
        Func<Task>[] calls =
        [
            () => service.GetAsync(noteId),
            () => service.ListPinnedAsync(personId),
            () => service.CreateAsync(new CreateNoteRequest(personId, "Note.")),
            () => service.UpdateAsync(noteId, new UpdateNoteRequest("Note.")),
            () => service.DeleteAsync(noteId),
            () => service.SetPinnedAsync(noteId, true),
        ];

        foreach (var call in calls)
        {
            await call.Should().ThrowAsync<UnauthenticatedUserException>();
        }

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static NoteService CreateService(RelioDbContext dbContext, string? userId) =>
        new(dbContext, new FakeCurrentUser(userId));

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RelioDbContext(options, TimeProvider.System);
    }

    private static async Task<Guid> AddPersonAsync(RelioDbContext dbContext, string ownerId)
    {
        var person = new Person { OwnerId = ownerId, FirstName = "Ada" };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }
}
