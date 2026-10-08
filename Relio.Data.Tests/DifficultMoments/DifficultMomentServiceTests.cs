using Microsoft.EntityFrameworkCore;
using Relio.Application.DifficultMoments;
using Relio.Application.Ownership;
using Relio.Application.Security;
using Relio.Data.DifficultMoments;
using Relio.Data.Tests.People;
using Relio.Domain;

namespace Relio.Data.Tests.DifficultMoments;

public class DifficultMomentServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task GetAsync_returns_null_when_not_found_or_foreign()
    {
        await using var dbContext = CreateDbContext();
        var personA = await AddPersonAsync(dbContext, UserA);
        var moment = await CreateService(dbContext, UserA).CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personA,
            OccurredOn = new DateOnly(2026, 5, 1),
            Description = "A difficult moment.",
        });

        var serviceB = CreateService(dbContext, UserB);
        (await serviceB.GetAsync(moment.Id)).Should().BeNull();
        (await serviceB.GetAsync(Guid.NewGuid())).Should().BeNull();

        var serviceA = CreateService(dbContext, UserA);
        var fetched = await serviceA.GetAsync(moment.Id);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(moment.Id);
        fetched.Description.Should().Be("A difficult moment.");
    }

    [Fact]
    public async Task ListForPersonAsync_returns_only_moments_for_requested_person_and_owner()
    {
        await using var dbContext = CreateDbContext();
        var personA1 = await AddPersonAsync(dbContext, UserA, "Alice");
        var personA2 = await AddPersonAsync(dbContext, UserA, "Ada");
        var personB = await AddPersonAsync(dbContext, UserB, "Bob");

        var serviceA = CreateService(dbContext, UserA);
        await serviceA.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personA1,
            OccurredOn = new DateOnly(2026, 5, 1),
            Description = "Alice moment 1",
        });
        await serviceA.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personA1,
            OccurredOn = new DateOnly(2026, 6, 1),
            Description = "Alice moment 2",
        });
        await serviceA.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personA2,
            OccurredOn = new DateOnly(2026, 5, 15),
            Description = "Ada moment",
        });

        var serviceB = CreateService(dbContext, UserB);
        await serviceB.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personB,
            OccurredOn = new DateOnly(2026, 5, 1),
            Description = "Bob moment",
        });

        var aliceMoments = await serviceA.ListForPersonAsync(personA1);
        aliceMoments.Should().HaveCount(2);
        aliceMoments[0].OccurredOn.Should().Be(new DateOnly(2026, 6, 1)); // Descending order
        aliceMoments[1].OccurredOn.Should().Be(new DateOnly(2026, 5, 1));

        (await serviceB.ListForPersonAsync(personA1)).Should().BeEmpty();
    }

    [Fact]
    public async Task ListOverviewAsync_filters_by_status_and_person_and_respects_archive_flag()
    {
        await using var dbContext = CreateDbContext();
        var person1 = await AddPersonAsync(dbContext, UserA, "Alice");
        var person2 = await AddPersonAsync(dbContext, UserA, "Ada");
        var archivedPerson = await AddPersonAsync(dbContext, UserA, "Archived", isArchived: true);

        var service = CreateService(dbContext, UserA);
        await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person1,
            OccurredOn = new DateOnly(2026, 5, 1),
            Description = "Open moment 1",
            Status = DifficultMomentStatus.Open,
        });
        await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person1,
            OccurredOn = new DateOnly(2026, 5, 2),
            Description = "Resolved moment 1",
            Status = DifficultMomentStatus.Resolved,
            ResolvedOn = new DateOnly(2026, 5, 3),
        });
        await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person2,
            OccurredOn = new DateOnly(2026, 5, 4),
            Description = "Open moment 2",
            Status = DifficultMomentStatus.Open,
        });
        await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = archivedPerson,
            OccurredOn = new DateOnly(2026, 5, 5),
            Description = "Archived person moment",
            Status = DifficultMomentStatus.Open,
        });

        // Default: excludes archived person
        var allActive = await service.ListOverviewAsync(new DifficultMomentFilterRequest());
        allActive.Should().HaveCount(3);

        // Filter by status Open
        var openActive = await service.ListOverviewAsync(new DifficultMomentFilterRequest { Status = DifficultMomentStatus.Open });
        openActive.Should().HaveCount(2);

        // Filter by person 1
        var person1Moments = await service.ListOverviewAsync(new DifficultMomentFilterRequest { PersonId = person1 });
        person1Moments.Should().HaveCount(2);

        // Filter by person 1 AND status Resolved
        var person1Resolved = await service.ListOverviewAsync(new DifficultMomentFilterRequest
        {
            PersonId = person1,
            Status = DifficultMomentStatus.Resolved,
        });
        person1Resolved.Should().ContainSingle().Which.Description.Should().Be("Resolved moment 1");

        // Include archived
        var includingArchived = await service.ListOverviewAsync(new DifficultMomentFilterRequest { IncludeArchived = true });
        includingArchived.Should().HaveCount(4);
    }

    [Fact]
    public async Task ListCandidatesForRecurrenceAsync_excludes_current_moment_and_other_people()
    {
        await using var dbContext = CreateDbContext();
        var person1 = await AddPersonAsync(dbContext, UserA, "Alice");
        var person2 = await AddPersonAsync(dbContext, UserA, "Ada");

        var service = CreateService(dbContext, UserA);
        var moment1 = await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person1,
            OccurredOn = new DateOnly(2026, 1, 1),
            Description = "First moment",
        });
        var moment2 = await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person1,
            OccurredOn = new DateOnly(2026, 2, 1),
            Description = "Second moment",
        });
        await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person2,
            OccurredOn = new DateOnly(2026, 3, 1),
            Description = "Other person moment",
        });

        // When creating a new moment for person1 (no exclude id):
        var candidatesForNew = await service.ListCandidatesForRecurrenceAsync(person1);
        candidatesForNew.Select(c => c.Id).Should().BeEquivalentTo([moment1.Id, moment2.Id]);

        // When editing moment2 for person1 (exclude moment2):
        var candidatesForEdit = await service.ListCandidatesForRecurrenceAsync(person1, excludeMomentId: moment2.Id);
        candidatesForEdit.Select(c => c.Id).Should().Equal([moment1.Id]);
    }

    [Fact]
    public async Task CreateAsync_rejects_foreign_person_or_foreign_recurrence_parent()
    {
        await using var dbContext = CreateDbContext();
        var personA = await AddPersonAsync(dbContext, UserA, "Alice");
        var personB = await AddPersonAsync(dbContext, UserB, "Bob");

        var serviceA = CreateService(dbContext, UserA);
        var momentA = await serviceA.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personA,
            OccurredOn = new DateOnly(2026, 1, 1),
            Description = "Moment A",
        });

        var serviceB = CreateService(dbContext, UserB);

        // Attempt to create moment on User A's person
        var actForeignPerson = () => serviceB.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personA,
            OccurredOn = new DateOnly(2026, 1, 1),
            Description = "Sneaky moment",
        });
        await actForeignPerson.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        // Attempt to link User A's moment as recurrence
        var actForeignRecurrence = () => serviceB.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personB,
            OccurredOn = new DateOnly(2026, 1, 1),
            Description = "Sneaky recurrence",
            RecurrenceOfId = momentA.Id,
        });
        await actForeignRecurrence.Should().ThrowAsync<ForeignEntityNotOwnedException>();
    }

    [Fact]
    public async Task Recurrence_linking_and_parent_recurrences_navigation_works()
    {
        await using var dbContext = CreateDbContext();
        var person = await AddPersonAsync(dbContext, UserA);
        var service = CreateService(dbContext, UserA);

        var original = await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person,
            OccurredOn = new DateOnly(2026, 1, 10),
            Description = "First disagreement about budget",
            Trigger = "Unexpected expenditure",
            Status = DifficultMomentStatus.Open,
        });

        var recurrence = await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person,
            OccurredOn = new DateOnly(2026, 2, 20),
            Description = "Recurring budget disagreement",
            Trigger = "Another surprise cost",
            Status = DifficultMomentStatus.Recurring,
            RecurrenceOfId = original.Id,
        });

        // Verify child
        var fetchedChild = await service.GetAsync(recurrence.Id);
        fetchedChild.Should().NotBeNull();
        fetchedChild!.RecurrenceOfId.Should().Be(original.Id);
        fetchedChild.RecurrenceOfDescription.Should().Be("First disagreement about budget");

        // Verify parent has child in Recurrences list
        var fetchedParent = await service.GetAsync(original.Id);
        fetchedParent.Should().NotBeNull();
        fetchedParent!.Recurrences.Should().ContainSingle();
        fetchedParent.Recurrences[0].Id.Should().Be(recurrence.Id);
        fetchedParent.Recurrences[0].Description.Should().Be("Recurring budget disagreement");
        fetchedParent.Recurrences[0].Status.Should().Be(DifficultMomentStatus.Recurring);
    }

    [Fact]
    public async Task UpdateAsync_updates_moment_and_returns_false_for_foreign()
    {
        await using var dbContext = CreateDbContext();
        var personA = await AddPersonAsync(dbContext, UserA);
        var serviceA = CreateService(dbContext, UserA);

        var moment = await serviceA.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = personA,
            OccurredOn = new DateOnly(2026, 1, 1),
            Description = "Original description",
            Status = DifficultMomentStatus.Open,
        });

        var serviceB = CreateService(dbContext, UserB);
        var foreignUpdate = await serviceB.UpdateAsync(moment.Id, new UpdateDifficultMomentRequest
        {
            OccurredOn = new DateOnly(2026, 1, 1),
            Description = "Hacked description",
            Status = DifficultMomentStatus.Open,
        });
        foreignUpdate.Should().BeFalse();

        var successUpdate = await serviceA.UpdateAsync(moment.Id, new UpdateDifficultMomentRequest
        {
            OccurredOn = new DateOnly(2026, 1, 2),
            Description = "Updated description",
            Trigger = "New trigger",
            Resolution = "New resolution",
            LessonsLearned = "New lesson",
            Status = DifficultMomentStatus.Resolved,
            ResolvedOn = new DateOnly(2026, 1, 3),
        });
        successUpdate.Should().BeTrue();

        var updated = await serviceA.GetAsync(moment.Id);
        updated.Should().NotBeNull();
        updated!.Description.Should().Be("Updated description");
        updated.Trigger.Should().Be("New trigger");
        updated.Resolution.Should().Be("New resolution");
        updated.LessonsLearned.Should().Be("New lesson");
        updated.Status.Should().Be(DifficultMomentStatus.Resolved);
        updated.ResolvedOn.Should().Be(new DateOnly(2026, 1, 3));
    }

    [Fact]
    public async Task DeleteAsync_deletes_moment_and_clears_recurrence_links_on_children()
    {
        await using var dbContext = CreateDbContext();
        var person = await AddPersonAsync(dbContext, UserA);
        var service = CreateService(dbContext, UserA);

        var parent = await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person,
            OccurredOn = new DateOnly(2026, 1, 1),
            Description = "Parent moment",
        });

        var child = await service.CreateAsync(new CreateDifficultMomentRequest
        {
            PersonId = person,
            OccurredOn = new DateOnly(2026, 2, 1),
            Description = "Child moment",
            RecurrenceOfId = parent.Id,
        });

        var serviceB = CreateService(dbContext, UserB);
        (await serviceB.DeleteAsync(parent.Id)).Should().BeFalse();

        (await service.DeleteAsync(parent.Id)).Should().BeTrue();

        (await service.GetAsync(parent.Id)).Should().BeNull();

        // Child should still exist, with RecurrenceOfId cleared
        var fetchedChild = await service.GetAsync(child.Id);
        fetchedChild.Should().NotBeNull();
        fetchedChild!.RecurrenceOfId.Should().BeNull();
    }

    [Fact]
    public async Task Anonymous_user_throws_UnauthenticatedUserException()
    {
        await using var dbContext = CreateDbContext();
        var personId = await AddPersonAsync(dbContext, UserA);
        var service = CreateService(dbContext, null);
        var momentId = Guid.NewGuid();

        Func<Task>[] calls =
        [
            () => service.GetAsync(momentId),
            () => service.ListForPersonAsync(personId),
            () => service.ListOverviewAsync(new DifficultMomentFilterRequest()),
            () => service.ListCandidatesForRecurrenceAsync(personId),
            () => service.CreateAsync(new CreateDifficultMomentRequest
            {
                PersonId = personId,
                OccurredOn = new DateOnly(2026, 1, 1),
                Description = "Description",
            }),
            () => service.UpdateAsync(momentId, new UpdateDifficultMomentRequest
            {
                OccurredOn = new DateOnly(2026, 1, 1),
                Description = "Description",
                Status = DifficultMomentStatus.Open,
            }),
            () => service.DeleteAsync(momentId),
        ];

        foreach (var call in calls)
        {
            await call.Should().ThrowAsync<UnauthenticatedUserException>();
        }
    }

    private static DifficultMomentService CreateService(RelioDbContext dbContext, string? userId) =>
        new(dbContext, new FakeCurrentUser(userId), TimeProvider.System);

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RelioDbContext(options, TimeProvider.System, FieldProtector);
    }

    private static async Task<Guid> AddPersonAsync(RelioDbContext dbContext, string ownerId, string name = "Ada", bool isArchived = false)
    {
        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = name,
            IsArchived = isArchived,
            ArchivedAtUtc = isArchived ? DateTime.UtcNow : null,
        };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }
}
