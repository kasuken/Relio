using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Ownership;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Reminders;
using Relio.Data.Time;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class ReminderSqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        Guid personA;
        Guid secondPersonA;
        Guid personB;
        Guid reminderA;
        Guid reminderB;
        Guid? reminderBToDelete = null;
        var today = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime);

        await using (var setup = fixture.CreateDbContext())
        {
            personA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Reminder A");
            secondPersonA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Second reminder A");
            personB = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Reminder B");
            reminderA = (await CreateService(setup, harness.OwnerA.Id, harness.Clock).CreateAsync(
                new CreateReminderRequest
                {
                    PersonId = personA,
                    Title = "A private follow-up",
                    DueDate = today,
                })).Id;
            reminderB = (await CreateService(setup, harness.OwnerB.Id, harness.Clock).CreateAsync(
                new CreateReminderRequest
                {
                    PersonId = personB,
                    Title = "B private follow-up",
                    DueDate = today.AddDays(1),
                })).Id;
        }

        await using var ownerBScope = harness.AsOwnerB();
        var remindersB = CreateService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock);
        (await remindersB.GetAsync(reminderA)).Should().BeNull();
        (await remindersB.GetAsync(Guid.NewGuid())).Should().BeNull();
        (await remindersB.UpdateAsync(reminderA, UpdateRequest(today.AddDays(2)))).Should().BeNull();
        (await remindersB.UpdateAsync(Guid.NewGuid(), UpdateRequest(today.AddDays(2)))).Should().BeNull();
        (await remindersB.CompleteAsync(reminderA)).Should().BeFalse();
        (await remindersB.CompleteAsync(Guid.NewGuid())).Should().BeFalse();
        (await remindersB.SnoozeAsync(reminderA, today.AddDays(3))).Should().BeFalse();
        (await remindersB.SnoozeAsync(Guid.NewGuid(), today.AddDays(3))).Should().BeFalse();
        (await remindersB.DeleteAsync(reminderA)).Should().BeFalse();
        (await remindersB.DeleteAsync(Guid.NewGuid())).Should().BeFalse();

        var listedB = await remindersB.ListAsync();
        listedB.Select(reminder => reminder.Id).Should().Equal(reminderB);
        var byPersonB = await remindersB.ListForPersonAsync(personB);
        byPersonB.Select(reminder => reminder.Id).Should().Equal(reminderB);
        var foreignPersonList = () => remindersB.ListForPersonAsync(secondPersonA);
        await foreignPersonList.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        var updatedReminderB = await remindersB.UpdateAsync(reminderB, UpdateRequest(today.AddDays(4)));
        updatedReminderB.Should().NotBeNull();
        updatedReminderB!.DueDate.Should().Be(today.AddDays(4));
        (await remindersB.SnoozeAsync(reminderB, today.AddDays(5))).Should().BeTrue();
        var snoozedReminderB = await remindersB.GetAsync(reminderB);
        snoozedReminderB.Should().NotBeNull();
        snoozedReminderB!.EffectiveDueDate.Should().Be(today.AddDays(5));
        (await remindersB.MarkContactedAsync(personB, today)).Should().BeTrue();
        (await remindersB.CompleteAsync(reminderB)).Should().BeTrue();
        (await remindersB.ListAsync()).Should().BeEmpty();
        (await remindersB.ListAsync(includeCompleted: true))
            .Select(reminder => reminder.Id).Should().ContainSingle().Which.Should().Be(reminderB);
        reminderBToDelete = (await remindersB.CreateAsync(new CreateReminderRequest
        {
            PersonId = personB,
            Title = "B reminder to delete",
            DueDate = today.AddDays(2),
        })).Id;
        (await remindersB.DeleteAsync(reminderBToDelete.Value)).Should().BeTrue();
        (await remindersB.GetAsync(reminderBToDelete.Value)).Should().BeNull();

        await using var verify = fixture.CreateDbContext();
        var storedA = await verify.Reminders.AsNoTracking().SingleAsync(reminder => reminder.Id == reminderA);
        storedA.Title.Should().Be("A private follow-up");
        storedA.DueDate.Should().Be(today);
        storedA.SnoozedUntilDate.Should().BeNull();
        storedA.IsCompleted.Should().BeFalse();
        var storedB = await verify.Reminders.AsNoTracking().SingleAsync(reminder => reminder.Id == reminderB);
        storedB.DueDate.Should().Be(today.AddDays(4));
        storedB.SnoozedUntilDate.Should().Be(today.AddDays(5));
        storedB.IsCompleted.Should().BeTrue();
        (await verify.Reminders.AsNoTracking().AnyAsync(reminder => reminder.Id == reminderBToDelete.Value))
            .Should().BeFalse();
        (await verify.People.AsNoTracking().SingleAsync(person => person.Id == personA))
            .LastContactedOn.Should().BeNull();
        (await verify.People.AsNoTracking().SingleAsync(person => person.Id == personB))
            .LastContactedOn.Should().Be(today);
    }

    [SqlServerFact]
    public async Task Foreign_person_ids_are_rejected_and_reminder_lists_exclude_archived_people()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        Guid personA;
        Guid otherPersonA;
        Guid personB;
        Guid archivedPersonA;
        Guid reminderA;
        var today = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime);

        await using (var setup = fixture.CreateDbContext())
        {
            personA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Active A");
            otherPersonA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Second active A");
            personB = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Active B");
            archivedPersonA = await TestDataFactory.CreatePersonAsync(
                setup,
                harness.OwnerA.Id,
                "Archived A",
                isArchived: true);
            reminderA = (await CreateService(setup, harness.OwnerA.Id, harness.Clock).CreateAsync(
                new CreateReminderRequest
                {
                    PersonId = personA,
                    Title = "A active reminder",
                    DueDate = today,
                })).Id;
            setup.Reminders.AddRange(
                new Reminder
                {
                    OwnerId = harness.OwnerA.Id,
                    PersonId = otherPersonA,
                    Title = "A second reminder",
                    DueDate = today.AddDays(-1),
                },
                new Reminder
                {
                    OwnerId = harness.OwnerA.Id,
                    PersonId = archivedPersonA,
                    Title = "Archived reminder",
                    DueDate = today.AddDays(-1),
                },
                new Reminder
                {
                    OwnerId = harness.OwnerB.Id,
                    PersonId = personB,
                    Title = "B reminder",
                    DueDate = today.AddDays(-1),
                });
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();
        }

        await using var ownerAScope = harness.AsOwnerA();
        var remindersA = CreateService(ownerAScope.DbContext, ownerAScope.CurrentUser, harness.Clock);
        var foreignPersonCreate = () => remindersA.CreateAsync(new CreateReminderRequest
        {
            PersonId = personB,
            Title = "Rejected foreign reminder",
            DueDate = today,
        });
        (await foreignPersonCreate.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .Which.EntityName.Should().Be(ForeignEntityNames.People);
        var missingPersonCreate = () => remindersA.CreateAsync(new CreateReminderRequest
        {
            PersonId = Guid.NewGuid(),
            Title = "Rejected missing reminder",
            DueDate = today,
        });
        (await missingPersonCreate.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .Which.EntityName.Should().Be(ForeignEntityNames.People);
        var archivedPersonCreate = () => remindersA.CreateAsync(new CreateReminderRequest
        {
            PersonId = archivedPersonA,
            Title = "Rejected archived reminder",
            DueDate = today,
        });
        (await archivedPersonCreate.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .Which.EntityName.Should().Be(ForeignEntityNames.People);
        var foreignPersonListError = await Assert.ThrowsAsync<ForeignEntityNotOwnedException>(
            () => remindersA.ListForPersonAsync(personB));
        var missingPersonListError = await Assert.ThrowsAsync<ForeignEntityNotOwnedException>(
            () => remindersA.ListForPersonAsync(Guid.NewGuid()));
        missingPersonListError.Message.Should().Be(foreignPersonListError.Message);
        (await remindersA.MarkContactedAsync(personB, today)).Should().BeFalse();
        (await remindersA.MarkContactedAsync(Guid.NewGuid(), today)).Should().BeFalse();

        var due = await remindersA.ListDueAsync(today);
        due.Select(reminder => reminder.PersonId).Should().BeEquivalentTo(new[] { personA, otherPersonA });
        due.Should().HaveCount(2);
        (await remindersA.ListForPersonAsync(archivedPersonA, includeCompleted: true))
            .Select(reminder => reminder.PersonId).Should().ContainSingle().Which.Should().Be(archivedPersonA);
        (await remindersA.GetAsync(reminderA)).Should().NotBeNull();

        await using var verify = fixture.CreateDbContext();
        (await verify.Reminders.CountAsync(reminder => reminder.OwnerId == harness.OwnerA.Id))
            .Should().Be(3, "the two rejected creates must not add rows");
        (await verify.People.AsNoTracking().SingleAsync(person => person.Id == personA))
            .LastContactedOn.Should().BeNull();
        (await verify.People.AsNoTracking().SingleAsync(person => person.Id == personB))
            .LastContactedOn.Should().BeNull();
    }

    [SqlServerFact]
    public async Task Birthday_and_reach_out_projections_are_owner_scoped_and_hide_archived_people()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var today = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime);
        Guid birthdayA;
        Guid birthdayB;
        Guid archivedBirthdayA;
        Guid reachOutA;
        Guid archivedReachOutA;
        Guid reachOutB;

        await using (var setup = fixture.CreateDbContext())
        {
            var profileA = await setup.UserProfiles.SingleAsync(profile => profile.OwnerId == harness.OwnerA.Id);
            profileA.DefaultBirthdayLeadDays = 1;
            var profileB = await setup.UserProfiles.SingleAsync(profile => profile.OwnerId == harness.OwnerB.Id);
            profileB.DefaultBirthdayLeadDays = 0;

            var personA = CreateBirthdayPerson(harness.OwnerA.Id, "Birthday A", today.AddDays(1));
            var personB = CreateBirthdayPerson(harness.OwnerB.Id, "Birthday B", today);
            var archived = CreateBirthdayPerson(
                harness.OwnerA.Id,
                "Archived birthday A",
                today,
                isArchived: true);
            birthdayA = personA.Id;
            birthdayB = personB.Id;
            archivedBirthdayA = archived.Id;

            var lastContact = today.AddDays(-40);
            var overdueA = new Person
            {
                OwnerId = harness.OwnerA.Id,
                FirstName = "Reach out A",
                StayInTouchCadenceDays = 30,
                LastContactedOn = lastContact,
            };
            var archivedOverdue = new Person
            {
                OwnerId = harness.OwnerA.Id,
                FirstName = "Archived reach out A",
                StayInTouchCadenceDays = 30,
                LastContactedOn = lastContact,
                IsArchived = true,
                ArchivedAtUtc = harness.Clock.GetUtcNow().UtcDateTime,
            };
            var overdueB = new Person
            {
                OwnerId = harness.OwnerB.Id,
                FirstName = "Reach out B",
                StayInTouchCadenceDays = 30,
                LastContactedOn = lastContact,
            };
            reachOutA = overdueA.Id;
            archivedReachOutA = archivedOverdue.Id;
            reachOutB = overdueB.Id;
            setup.People.AddRange(personA, personB, archived, overdueA, archivedOverdue, overdueB);
            await setup.SaveChangesAsync();
        }

        await using var ownerAScope = harness.AsOwnerA();
        var serviceA = CreateService(ownerAScope.DbContext, ownerAScope.CurrentUser, harness.Clock);
        (await serviceA.ListDueBirthdaysAsync())
            .Select(birthday => birthday.PersonId).Should().ContainSingle().Which.Should().Be(birthdayA);
        (await serviceA.ListUpcomingBirthdaysAsync(daysAhead: 30))
            .Select(birthday => birthday.PersonId).Should().ContainSingle().Which.Should().Be(birthdayA);
        (await serviceA.ListOverdueReachOutsAsync())
            .Select(item => item.PersonId).Should().ContainSingle().Which.Should().Be(reachOutA);

        await using var ownerBScope = harness.AsOwnerB();
        var serviceB = CreateService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock);
        (await serviceB.ListDueBirthdaysAsync())
            .Select(birthday => birthday.PersonId).Should().ContainSingle().Which.Should().Be(birthdayB);
        (await serviceB.ListUpcomingBirthdaysAsync(daysAhead: 30))
            .Select(birthday => birthday.PersonId).Should().ContainSingle().Which.Should().Be(birthdayB);
        (await serviceB.ListOverdueReachOutsAsync())
            .Select(item => item.PersonId).Should().ContainSingle().Which.Should().Be(reachOutB);
        (await serviceA.ListDueBirthdaysAsync()).Select(item => item.PersonId)
            .Should().NotContain(archivedBirthdayA);
        (await serviceA.ListOverdueReachOutsAsync()).Select(item => item.PersonId)
            .Should().NotContain(archivedReachOutA);
    }

    private static ReminderService CreateService(
        RelioDbContext dbContext,
        string ownerId,
        TimeProvider clock) =>
        CreateService(dbContext, new FakeCurrentUser(ownerId), clock);

    private static ReminderService CreateService(
        RelioDbContext dbContext,
        ICurrentUser currentUser,
        TimeProvider clock) =>
        new(dbContext, currentUser, new UserTimeZoneService(dbContext, currentUser, clock), clock);

    private static UpdateReminderRequest UpdateRequest(DateOnly dueDate) =>
        new()
        {
            Title = "Updated synthetic reminder",
            DueDate = dueDate,
        };

    private static Person CreateBirthdayPerson(
        string ownerId,
        string firstName,
        DateOnly birthday,
        bool isArchived = false) =>
        new()
        {
            OwnerId = ownerId,
            FirstName = firstName,
            BirthdayMonth = birthday.Month,
            BirthdayDay = birthday.Day,
            IsArchived = isArchived,
            ArchivedAtUtc = isArchived ? SqlIsolationTestHarness.FixedInstant.UtcDateTime : null,
        };
}
