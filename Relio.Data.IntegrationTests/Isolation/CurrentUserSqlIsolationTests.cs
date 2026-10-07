using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.Dashboard;
using Relio.Application.Interactions;
using Relio.Application.Notes;
using Relio.Application.Onboarding;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Application.Portability;
using Relio.Application.Profile;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Application.Timeline;
using Relio.Data.Administration;
using Relio.Data.Dashboard;
using Relio.Data.Identity;
using Relio.Data.Interactions;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Notes;
using Relio.Data.Onboarding;
using Relio.Data.People;
using Relio.Data.Portability;
using Relio.Data.Profile;
using Relio.Data.Reminders;
using Relio.Data.Time;
using Relio.Data.Timeline;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class CurrentUserSqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Every_current_user_data_service_rejects_anonymous_calls_before_data_access()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        await using var scope = harness.As(null);
        var user = scope.CurrentUser;
        var db = scope.DbContext;
        var today = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime);
        var id = Guid.NewGuid();

        var people = new PeopleService(db, user, harness.Clock);
        await RequiresUserAsync(() => people.GetAsync(id));
        await RequiresUserAsync(() => people.ListAsync());
        await RequiresUserAsync(() => people.ListPageAsync(new PeopleListQuery()));
        await RequiresUserAsync(() => people.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery()));
        await RequiresUserAsync(() => people.CreateAsync(new CreatePersonRequest { FirstName = "Synthetic" }));
        await RequiresUserAsync(() => people.UpdateAsync(id, new UpdatePersonRequest { FirstName = "Synthetic" }));
        await RequiresUserAsync(() => people.ArchiveAsync(id));
        await RequiresUserAsync(() => people.RestoreAsync(id));
        await RequiresUserAsync(() => people.DeleteAsync(id));

        var dashboard = new DashboardService(db, user, harness.Clock);
        await RequiresUserAsync(() => dashboard.GetAsync());

        var onboarding = new OnboardingService(db, user);
        await RequiresUserAsync(() => onboarding.GetStateAsync());
        await RequiresUserAsync(() => onboarding.DismissAsync());

        var interactions = new InteractionService(db, user, harness.Clock);
        await RequiresUserAsync(() => interactions.GetAsync(id));
        await RequiresUserAsync(() => interactions.ListParticipantCandidatesAsync(id));
        await RequiresUserAsync(() => interactions.CreateAsync(new CreateInteractionRequest
        {
            ProfilePersonId = id,
            OccurredOn = today,
            Kind = InteractionKind.Call,
            Description = "Synthetic interaction",
            ParticipantIds = [id],
        }));
        await RequiresUserAsync(() => interactions.UpdateAsync(id, new UpdateInteractionRequest
        {
            OccurredOn = today,
            Kind = InteractionKind.Call,
            Description = "Synthetic interaction",
            ParticipantIds = [id],
        }));
        await RequiresUserAsync(() => interactions.DeleteAsync(id));

        var notes = new NoteService(db, user);
        await RequiresUserAsync(() => notes.GetAsync(id));
        await RequiresUserAsync(() => notes.ListPinnedAsync(id));
        await RequiresUserAsync(() => notes.CreateAsync(new CreateNoteRequest(id, "Synthetic note")));
        await RequiresUserAsync(() => notes.UpdateAsync(id, new UpdateNoteRequest("Synthetic note")));
        await RequiresUserAsync(() => notes.DeleteAsync(id));
        await RequiresUserAsync(() => notes.SetPinnedAsync(id, true));

        var timeline = new PersonTimelineService(db, user);
        await RequiresUserAsync(() => timeline.GetPageAsync(id));

        var portability = new UserDataPortabilityService(db, user, harness.Clock);
        await RequiresUserAsync(() => portability.ExportAsync());
        await RequiresUserAsync(() => portability.ExportPeopleVCardAsync());
        await RequiresUserAsync(() => portability.RestoreAsync(null!));

        var imports = new PeopleImportService(db, user, harness.Clock);
        await RequiresUserAsync(() => imports.PreviewAsync(new ImportReadResult([], 0, false, 0)));
        await RequiresUserAsync(() => imports.ImportAsync([]));

        var merge = new PersonMergeService(db, user, harness.Clock);
        await RequiresUserAsync(() => merge.ListCandidatesAsync(id));
        await RequiresUserAsync(() => merge.MergeAsync(new MergePeopleRequest
        {
            PrimaryId = id,
            DuplicateId = Guid.NewGuid(),
        }));

        var relationshipTypes = new RelationshipTypeService(db, user);
        await RequiresUserAsync(() => relationshipTypes.ListAsync());
        await RequiresUserAsync(() => relationshipTypes.ListWithUsageAsync());
        await RequiresUserAsync(() => relationshipTypes.CreateAsync("Synthetic"));
        await RequiresUserAsync(() => relationshipTypes.RenameAsync(id, "Synthetic"));
        await RequiresUserAsync(() => relationshipTypes.DeleteAsync(id, null));

        var tags = new TagService(db, user);
        await RequiresUserAsync(() => tags.ListAsync());
        await RequiresUserAsync(() => tags.ListWithUsageAsync());
        await RequiresUserAsync(() => tags.CreateAsync("Synthetic"));
        await RequiresUserAsync(() => tags.RenameAsync(id, "Synthetic"));
        await RequiresUserAsync(() => tags.DeleteAsync(id));

        var timeZones = new UserTimeZoneService(db, user, harness.Clock);
        await RequiresUserAsync(() => timeZones.GetTimeZoneAsync());
        await RequiresUserAsync(() => timeZones.GetTodayAsync());
        await RequiresUserAsync(() => timeZones.SetTimeZoneAsync("UTC"));
        await RequiresUserAsync(() => timeZones.IsDueTodayAsync(today));
        await RequiresUserAsync(() => timeZones.IsOverdueAsync(today));

        var profile = new UserProfileService(db, user);
        await RequiresUserAsync(() => profile.GetDisplayNameAsync());
        await RequiresUserAsync(() => profile.SetDisplayNameAsync("Synthetic"));

        var reminders = CreateReminderService(db, user, harness.Clock);
        await RequiresUserAsync(() => reminders.GetAsync(id));
        await RequiresUserAsync(() => reminders.ListAsync());
        await RequiresUserAsync(() => reminders.ListForPersonAsync(id));
        await RequiresUserAsync(() => reminders.ListDueAsync(today));
        await RequiresUserAsync(() => reminders.CreateAsync(new CreateReminderRequest
        {
            PersonId = id,
            Title = "Synthetic reminder",
            DueDate = today,
        }));
        await RequiresUserAsync(() => reminders.UpdateAsync(id, new UpdateReminderRequest
        {
            Title = "Synthetic reminder",
            DueDate = today,
        }));
        await RequiresUserAsync(() => reminders.CompleteAsync(id));
        await RequiresUserAsync(() => reminders.SnoozeAsync(id, today.AddDays(1)));
        await RequiresUserAsync(() => reminders.DeleteAsync(id));
        await RequiresUserAsync(() => reminders.ListDueBirthdaysAsync());
        await RequiresUserAsync(() => reminders.ListUpcomingBirthdaysAsync());
        await RequiresUserAsync(() => reminders.ListOverdueReachOutsAsync());
        await RequiresUserAsync(() => reminders.MarkContactedAsync(id, today));

        var preferences = new NotificationPreferencesService(db, user);
        await RequiresUserAsync(() => preferences.GetPreferencesAsync());
        await RequiresUserAsync(() => preferences.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
            ReminderEmailDelivery.None,
            BirthdayRemindersEnabled: false,
            DefaultBirthdayLeadDays: 0)));

        var twoFactor = new TwoFactorStatusService(db, user);
        await RequiresUserAsync(() => twoFactor.GetStatusAsync());

        using var identityServices = scope.CreateIdentityServices();
        var administration = new UserAdministrationService(
            db,
            identityServices.GetRequiredService<UserManager<RelioUser>>(),
            user,
            harness.Clock,
            Options.Create(new RegistrationOptions { Mode = RegistrationMode.InviteOnly }),
            NullLogger<UserAdministrationService>.Instance);
        await RequiresUserAsync(() => administration.ListAccountsAsync());
        await RequiresUserAsync(() => administration.DisableAccountAsync("synthetic-user"));
        await RequiresUserAsync(() => administration.EnableAccountAsync("synthetic-user"));
        await RequiresUserAsync(() => administration.ListPendingInvitationsAsync());
        await RequiresUserAsync(() => administration.CreateInvitationAsync("invite@example.com"));
        await RequiresUserAsync(() => administration.RevokeInvitationAsync(id));

        db.ChangeTracker.Entries().Should().BeEmpty("anonymous calls fail before loading or tracking data");
    }

    [SqlServerFact]
    public async Task Administrator_role_does_not_bypass_owner_scoping_for_relationship_memory()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture, makeOwnerAAdministrator: true);
        Guid personB;
        Guid relationshipTypeB;
        Guid tagB;
        Guid contactMethodB;
        Guid noteB;
        Guid interactionB;
        Guid reminderB;

        await using (var setup = fixture.CreateDbContext())
        {
            relationshipTypeB = await TestDataFactory.CreateRelationshipTypeAsync(
                setup,
                harness.OwnerB.Id,
                "Private B relationship",
                RelationshipType.DefaultNames.Count);
            tagB = await TestDataFactory.CreateTagAsync(setup, harness.OwnerB.Id, "Private B tag");
            personB = await TestDataFactory.CreatePersonAsync(
                setup,
                harness.OwnerB.Id,
                "Private B",
                relationshipTypeId: relationshipTypeB,
                tagIds: [tagB]);
            contactMethodB = await TestDataFactory.CreateContactMethodAsync(
                setup,
                harness.OwnerB.Id,
                personB,
                "private-b@example.com");
            noteB = (await new NoteService(setup, new FakeCurrentUser(harness.OwnerB.Id))
                .CreateAsync(new CreateNoteRequest(personB, "Owner B note"))).Id;
            interactionB = await new InteractionService(
                    setup,
                    new FakeCurrentUser(harness.OwnerB.Id),
                    harness.Clock)
                .CreateAsync(new CreateInteractionRequest
                {
                    ProfilePersonId = personB,
                    OccurredOn = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime),
                    Kind = InteractionKind.Call,
                    Description = "Owner B interaction",
                    ParticipantIds = [personB],
                });
            reminderB = (await CreateReminderService(setup, new FakeCurrentUser(harness.OwnerB.Id), harness.Clock)
                .CreateAsync(new CreateReminderRequest
                {
                    PersonId = personB,
                    Title = "Owner B reminder",
                    DueDate = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime),
                })).Id;
        }

        await using var adminScope = harness.AsOwnerA();
        var adminPeople = new PeopleService(adminScope.DbContext, adminScope.CurrentUser, harness.Clock);
        (await adminPeople.GetAsync(personB)).Should().BeNull();
        (await adminPeople.ListAsync()).Should().BeEmpty();
        (await adminPeople.ListPageAsync(new PeopleListQuery { IncludeArchived = true }))
            .People.TotalCount.Should().Be(0);

        var adminRelationshipTypes = new RelationshipTypeService(adminScope.DbContext, adminScope.CurrentUser);
        (await adminRelationshipTypes.ListAsync()).Select(type => type.Id).Should().NotContain(relationshipTypeB);
        var adminTags = new TagService(adminScope.DbContext, adminScope.CurrentUser);
        (await adminTags.ListAsync()).Select(tag => tag.Id).Should().NotContain(tagB);

        var adminNotes = new NoteService(adminScope.DbContext, adminScope.CurrentUser);
        (await adminNotes.GetAsync(noteB)).Should().BeNull();
        (await adminNotes.ListPinnedAsync(personB)).Should().BeEmpty();

        var adminInteractions = new InteractionService(
            adminScope.DbContext,
            adminScope.CurrentUser,
            harness.Clock);
        (await adminInteractions.GetAsync(interactionB)).Should().BeNull();
        (await adminInteractions.ListParticipantCandidatesAsync(personB)).Should().BeEmpty();

        var adminTimeline = new PersonTimelineService(adminScope.DbContext, adminScope.CurrentUser);
        (await adminTimeline.GetPageAsync(personB)).Should().BeNull();

        var adminReminders = CreateReminderService(
            adminScope.DbContext,
            adminScope.CurrentUser,
            harness.Clock);
        (await adminReminders.GetAsync(reminderB)).Should().BeNull();

        var adminMerge = new PersonMergeService(
            adminScope.DbContext,
            adminScope.CurrentUser,
            harness.Clock);
        (await adminMerge.ListCandidatesAsync(personB)).Should().BeNull();

        var adminExport = await new UserDataPortabilityService(
                adminScope.DbContext,
                adminScope.CurrentUser,
                harness.Clock)
            .ExportAsync();
        adminExport.People.Should().BeEmpty();
        adminExport.Tags.Select(tag => tag.Id).Should().NotContain(tagB);
        adminExport.RelationshipTypes.Select(type => type.Id).Should().NotContain(relationshipTypeB);
        adminExport.Interactions.Should().BeEmpty();
        adminExport.Notes.Should().BeEmpty();
        adminExport.Reminders.Should().BeEmpty();

        var adminDashboard = new DashboardService(adminScope.DbContext, adminScope.CurrentUser, harness.Clock);
        var dashboard = await adminDashboard.GetAsync();
        dashboard.ActivePeopleCount.Should().Be(0);
        dashboard.RecentlyAddedPeople.Should().BeEmpty();
        dashboard.RecentInteractions.Should().BeEmpty();
        dashboard.UpcomingReminders.Should().BeEmpty();

        await using var ownerBScope = harness.AsOwnerB();
        (await new PeopleService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock)
            .GetAsync(personB)).Should().NotBeNull();
        (await new NoteService(ownerBScope.DbContext, ownerBScope.CurrentUser).GetAsync(noteB))
            .Should().NotBeNull();
        (await new InteractionService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock)
            .GetAsync(interactionB)).Should().NotBeNull();
        (await CreateReminderService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock)
            .GetAsync(reminderB)).Should().NotBeNull();
        (await new RelationshipTypeService(ownerBScope.DbContext, ownerBScope.CurrentUser).ListAsync())
            .Select(type => type.Id).Should().Contain(relationshipTypeB);
        (await new TagService(ownerBScope.DbContext, ownerBScope.CurrentUser).ListAsync())
            .Select(tag => tag.Id).Should().Contain(tagB);
        (await new PeopleService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock)
            .GetAsync(personB))!.ContactMethods.Select(contact => contact.Id).Should().Contain(contactMethodB);
    }

    [SqlServerFact]
    public async Task Timeline_reads_only_the_current_owner_and_keeps_archived_people_timeline()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        Guid personA;
        Guid personB;
        Guid noteA;
        Guid interactionA;
        Guid noteB;
        Guid interactionB;

        await using (var setup = fixture.CreateDbContext())
        {
            personA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Timeline A");
            personB = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Timeline B");
            noteA = (await new NoteService(setup, new FakeCurrentUser(harness.OwnerA.Id))
                .CreateAsync(new CreateNoteRequest(personA, "A timeline note"))).Id;
            interactionA = await CreateInteractionAsync(
                setup,
                harness,
                harness.OwnerA.Id,
                personA,
                "A timeline interaction");
            noteB = (await new NoteService(setup, new FakeCurrentUser(harness.OwnerB.Id))
                .CreateAsync(new CreateNoteRequest(personB, "B timeline note"))).Id;
            interactionB = await CreateInteractionAsync(
                setup,
                harness,
                harness.OwnerB.Id,
                personB,
                "B timeline interaction");
            (await new PeopleService(setup, new FakeCurrentUser(harness.OwnerA.Id), harness.Clock)
                .ArchiveAsync(personA)).Should().BeTrue();
        }

        await using var ownerAScope = harness.AsOwnerA();
        var timelineA = new PersonTimelineService(ownerAScope.DbContext, ownerAScope.CurrentUser);
        var pageA = await timelineA.GetPageAsync(personA);
        pageA.Should().NotBeNull();
        pageA!.Items.Select(item => item.Id).Should().BeEquivalentTo(new[] { noteA, interactionA });
        (await timelineA.GetPageAsync(personB)).Should().BeNull();
        (await timelineA.GetPageAsync(Guid.NewGuid())).Should().BeNull();

        await using var ownerBScope = harness.AsOwnerB();
        var pageB = await new PersonTimelineService(ownerBScope.DbContext, ownerBScope.CurrentUser)
            .GetPageAsync(personB);
        pageB.Should().NotBeNull();
        pageB!.Items.Select(item => item.Id).Should().BeEquivalentTo(new[] { noteB, interactionB });
        pageB.Items.Select(item => item.Id).Should().NotContain(noteA);
        pageB.Items.Select(item => item.Id).Should().NotContain(interactionA);
    }

    [SqlServerFact]
    public async Task Invalid_interaction_participants_and_foreign_notes_leave_both_owners_unchanged()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        Guid personA;
        Guid secondPersonA;
        Guid personB;
        Guid interactionA;
        Guid interactionB;
        Guid noteA;
        Guid noteB;
        var today = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime);

        await using (var setup = fixture.CreateDbContext())
        {
            personA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Aggregate A");
            secondPersonA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Second A");
            personB = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Aggregate B");
            interactionA = await new InteractionService(
                    setup,
                    new FakeCurrentUser(harness.OwnerA.Id),
                    harness.Clock)
                .CreateAsync(new CreateInteractionRequest
                {
                    ProfilePersonId = personA,
                    OccurredOn = today,
                    Kind = InteractionKind.Call,
                    Description = "A interaction",
                    ParticipantIds = [personA, secondPersonA],
                });
            interactionB = await new InteractionService(
                    setup,
                    new FakeCurrentUser(harness.OwnerB.Id),
                    harness.Clock)
                .CreateAsync(new CreateInteractionRequest
                {
                    ProfilePersonId = personB,
                    OccurredOn = today,
                    Kind = InteractionKind.Meeting,
                    Description = "B interaction",
                    ParticipantIds = [personB],
                });
            noteA = (await new NoteService(setup, new FakeCurrentUser(harness.OwnerA.Id))
                .CreateAsync(new CreateNoteRequest(personA, "A note", IsPinned: true))).Id;
            noteB = (await new NoteService(setup, new FakeCurrentUser(harness.OwnerB.Id))
                .CreateAsync(new CreateNoteRequest(personB, "B note", IsPinned: true))).Id;
        }

        await using var ownerAScope = harness.AsOwnerA();
        var interactionsA = new InteractionService(ownerAScope.DbContext, ownerAScope.CurrentUser, harness.Clock);
        var rejectedUpdate = () => interactionsA.UpdateAsync(interactionA, new UpdateInteractionRequest
        {
            OccurredOn = today.AddDays(-1),
            Kind = InteractionKind.Message,
            Description = "Rejected update",
            ParticipantIds = [personA, personB],
        });
        (await rejectedUpdate.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .Which.EntityName.Should().Be(ForeignEntityNames.People);
        var candidatesA = await interactionsA.ListParticipantCandidatesAsync(personA);
        candidatesA.Select(person => person.PersonId).Should()
            .BeEquivalentTo(new[] { personA, secondPersonA });

        await using var ownerBScope = harness.AsOwnerB();
        var interactionsB = new InteractionService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock);
        (await interactionsB.GetAsync(interactionA)).Should().BeNull();
        (await interactionsB.GetAsync(Guid.NewGuid())).Should().BeNull();
        (await interactionsB.UpdateAsync(interactionA, new UpdateInteractionRequest
        {
            OccurredOn = today,
            Kind = InteractionKind.Call,
            Description = "No access",
            ParticipantIds = [personB],
        })).Should().BeFalse();
        (await interactionsB.UpdateAsync(Guid.NewGuid(), new UpdateInteractionRequest
        {
            OccurredOn = today,
            Kind = InteractionKind.Call,
            Description = "No access",
            ParticipantIds = [personB],
        })).Should().BeFalse();
        (await interactionsB.DeleteAsync(interactionA)).Should().BeFalse();
        (await interactionsB.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
        (await interactionsB.GetAsync(interactionB)).Should().NotBeNull();

        var notesB = new NoteService(ownerBScope.DbContext, ownerBScope.CurrentUser);
        (await notesB.GetAsync(noteA)).Should().BeNull();
        (await notesB.GetAsync(Guid.NewGuid())).Should().BeNull();
        (await notesB.ListPinnedAsync(personA)).Should().BeEmpty();
        (await notesB.ListPinnedAsync(Guid.NewGuid())).Should().BeEmpty();
        (await notesB.UpdateAsync(noteA, new UpdateNoteRequest("No access"))).Should().BeFalse();
        (await notesB.UpdateAsync(Guid.NewGuid(), new UpdateNoteRequest("No access"))).Should().BeFalse();
        (await notesB.SetPinnedAsync(noteA, false)).Should().BeFalse();
        (await notesB.DeleteAsync(noteA)).Should().BeFalse();
        var foreignPersonError = await Assert.ThrowsAsync<ForeignEntityNotOwnedException>(
            () => notesB.CreateAsync(new CreateNoteRequest(personA, "No access")));
        foreignPersonError.EntityName.Should().Be(ForeignEntityNames.People);
        var missingPersonError = await Assert.ThrowsAsync<ForeignEntityNotOwnedException>(
            () => notesB.CreateAsync(new CreateNoteRequest(Guid.NewGuid(), "No access")));
        missingPersonError.Message.Should().Be(foreignPersonError.Message);
        var ownNote = await notesB.CreateAsync(new CreateNoteRequest(personB, "B follow-up"));
        (await notesB.GetAsync(ownNote.Id)).Should().NotBeNull();

        await using var verify = fixture.CreateDbContext();
        var storedInteraction = await verify.Interactions.AsNoTracking()
            .SingleAsync(interaction => interaction.Id == interactionA);
        storedInteraction.Description.Should().Be("A interaction");
        storedInteraction.OccurredOn.Should().Be(today);
        (await verify.InteractionParticipants.AsNoTracking()
            .Where(participant => participant.OwnerId == harness.OwnerA.Id
                && participant.InteractionId == interactionA)
            .Select(participant => participant.PersonId)
            .ToListAsync())
            .Should().BeEquivalentTo(new[] { personA, secondPersonA });
        var storedNoteA = await new NoteService(verify, new FakeCurrentUser(harness.OwnerA.Id)).GetAsync(noteA);
        storedNoteA.Should().NotBeNull();
        storedNoteA!.Text.Should().Be("A note");
        var storedNoteB = await new NoteService(verify, new FakeCurrentUser(harness.OwnerB.Id)).GetAsync(noteB);
        storedNoteB.Should().NotBeNull();
        storedNoteB!.Text.Should().Be("B note");
        (await verify.Interactions.AsNoTracking().SingleAsync(interaction => interaction.Id == interactionB))
            .Description.Should().Be("B interaction");
    }

    [SqlServerFact]
    public async Task Label_lists_and_mutations_are_owner_scoped_and_foreign_reassignment_is_rejected()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        Guid tagA;
        Guid tagB;
        Guid relationshipA;
        Guid relationshipB;
        Guid personA;

        await using (var setup = fixture.CreateDbContext())
        {
            tagA = await TestDataFactory.CreateTagAsync(setup, harness.OwnerA.Id, "A-only");
            tagB = await TestDataFactory.CreateTagAsync(setup, harness.OwnerB.Id, "B-only");
            relationshipA = await TestDataFactory.CreateRelationshipTypeAsync(
                setup,
                harness.OwnerA.Id,
                "A-only relationship");
            relationshipB = await TestDataFactory.CreateRelationshipTypeAsync(
                setup,
                harness.OwnerB.Id,
                "B-only relationship");
            personA = await TestDataFactory.CreatePersonAsync(
                setup,
                harness.OwnerA.Id,
                "Label owner A",
                relationshipTypeId: relationshipA,
                tagIds: [tagA]);
        }

        await using var ownerAScope = harness.AsOwnerA();
        var tagsA = new TagService(ownerAScope.DbContext, ownerAScope.CurrentUser);
        var listedTagsA = (await tagsA.ListAsync()).Select(tag => tag.Id).ToArray();
        listedTagsA.Should().Contain(tagA);
        listedTagsA.Should().NotContain(tagB);

        var relationshipTypesA = new RelationshipTypeService(ownerAScope.DbContext, ownerAScope.CurrentUser);
        var listedRelationshipTypesA = (await relationshipTypesA.ListAsync()).Select(type => type.Id).ToArray();
        listedRelationshipTypesA.Should().Contain(relationshipA);
        listedRelationshipTypesA.Should().NotContain(relationshipB);

        await using var ownerBScope = harness.AsOwnerB();
        var tagsB = new TagService(ownerBScope.DbContext, ownerBScope.CurrentUser);
        var listedTagsB = (await tagsB.ListAsync()).Select(tag => tag.Id).ToArray();
        listedTagsB.Should().Contain(tagB);
        listedTagsB.Should().NotContain(tagA);
        (await tagsB.RenameAsync(tagA, "Attempted rename")).Should().BeFalse();
        (await tagsB.RenameAsync(Guid.NewGuid(), "Attempted rename")).Should().BeFalse();
        (await tagsB.DeleteAsync(tagA)).Should().BeFalse();
        (await tagsB.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
        var ownTag = await tagsB.CreateAsync("B-created");
        (await tagsB.RenameAsync(ownTag.Id, "B-renamed")).Should().BeTrue();

        var relationshipTypesB = new RelationshipTypeService(ownerBScope.DbContext, ownerBScope.CurrentUser);
        (await relationshipTypesB.RenameAsync(relationshipA, "Attempted rename")).Should().BeFalse();
        (await relationshipTypesB.RenameAsync(Guid.NewGuid(), "Attempted rename")).Should().BeFalse();
        (await relationshipTypesB.DeleteAsync(relationshipA, null)).Should().BeFalse();
        (await relationshipTypesB.DeleteAsync(Guid.NewGuid(), null)).Should().BeFalse();
        var ownType = await relationshipTypesB.CreateAsync("B-created relationship");
        (await relationshipTypesB.RenameAsync(ownType.Id, "B-renamed relationship")).Should().BeTrue();

        var foreignReassignment = () => relationshipTypesA.DeleteAsync(relationshipA, relationshipB);
        (await foreignReassignment.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .Which.EntityName.Should().Be(ForeignEntityNames.RelationshipTypes);

        await using var verify = fixture.CreateDbContext();
        (await verify.Tags.AsNoTracking().SingleAsync(tag => tag.Id == tagA)).Name.Should().Be("A-only");
        (await verify.Tags.AsNoTracking().SingleAsync(tag => tag.Id == tagB)).Name.Should().Be("B-only");
        var unchangedPerson = await verify.People.AsNoTracking().SingleAsync(person => person.Id == personA);
        unchangedPerson.RelationshipTypeId.Should().Be(relationshipA);
        (await verify.People.Include(person => person.Tags).AsNoTracking()
            .SingleAsync(person => person.Id == personA))
            .Tags.Select(tag => tag.Id).Should().ContainSingle().Which.Should().Be(tagA);
        (await verify.RelationshipTypes.AsNoTracking().AnyAsync(type => type.Id == relationshipA))
            .Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Profile_time_zone_notification_and_two_factor_status_are_per_user()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);

        await using (var setup = fixture.CreateDbContext())
        {
            using var identityServices = SqlIsolationTestHarness.CreateIdentityServices(setup);
            var userManager = identityServices.GetRequiredService<UserManager<RelioUser>>();
            var ownerA = await userManager.FindByIdAsync(harness.OwnerA.Id);
            ownerA.Should().NotBeNull();
            (await userManager.ResetAuthenticatorKeyAsync(ownerA!)).Succeeded.Should().BeTrue();
            (await userManager.TurnOnTwoFactorAsync(ownerA!)).Should().NotBeNull();
        }

        await using (var ownerAScope = harness.AsOwnerA())
        {
            var profile = new UserProfileService(ownerAScope.DbContext, ownerAScope.CurrentUser);
            await profile.SetDisplayNameAsync("Account A");
            await new UserTimeZoneService(ownerAScope.DbContext, ownerAScope.CurrentUser, harness.Clock)
                .SetTimeZoneAsync("Pacific/Kiritimati");
            var preferences = new NotificationPreferencesService(ownerAScope.DbContext, ownerAScope.CurrentUser);
            var before = await preferences.GetPreferencesAsync();
            before.UnsubscribeToken.Should().NotBeNullOrWhiteSpace();
            await preferences.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
                ReminderEmailDelivery.None,
                BirthdayRemindersEnabled: false,
                DefaultBirthdayLeadDays: 9));
        }

        await using (var ownerBScope = harness.AsOwnerB())
        {
            var profile = new UserProfileService(ownerBScope.DbContext, ownerBScope.CurrentUser);
            await profile.SetDisplayNameAsync("Account B");
            await new UserTimeZoneService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock)
                .SetTimeZoneAsync("Pacific/Pago_Pago");
            var preferences = new NotificationPreferencesService(ownerBScope.DbContext, ownerBScope.CurrentUser);
            var before = await preferences.GetPreferencesAsync();
            before.UnsubscribeToken.Should().NotBeNullOrWhiteSpace();
            await preferences.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
                ReminderEmailDelivery.Immediate,
                BirthdayRemindersEnabled: true,
                DefaultBirthdayLeadDays: 2));
        }

        await using var verifyA = harness.AsOwnerA();
        (await new UserProfileService(verifyA.DbContext, verifyA.CurrentUser).GetDisplayNameAsync())
            .Should().Be("Account A");
        var timeZoneA = new UserTimeZoneService(verifyA.DbContext, verifyA.CurrentUser, harness.Clock);
        (await timeZoneA.GetTimeZoneAsync()).Id.Should().Be("Pacific/Kiritimati");
        (await timeZoneA.GetTodayAsync()).Should().Be(new DateOnly(2026, 10, 7));
        (await timeZoneA.IsDueTodayAsync(new DateOnly(2026, 10, 7))).Should().BeTrue();
        (await timeZoneA.IsOverdueAsync(new DateOnly(2026, 10, 6))).Should().BeTrue();
        var preferencesA = await new NotificationPreferencesService(verifyA.DbContext, verifyA.CurrentUser)
            .GetPreferencesAsync();
        preferencesA.Delivery.Should().Be(ReminderEmailDelivery.None);
        preferencesA.BirthdayRemindersEnabled.Should().BeFalse();
        preferencesA.DefaultBirthdayLeadDays.Should().Be(9);
        (await new TwoFactorStatusService(verifyA.DbContext, verifyA.CurrentUser).GetStatusAsync())
            .Should().Be(new TwoFactorStatus(IsEnabled: true, RecoveryCodesLeft: 10));

        await using var verifyB = harness.AsOwnerB();
        (await new UserProfileService(verifyB.DbContext, verifyB.CurrentUser).GetDisplayNameAsync())
            .Should().Be("Account B");
        var timeZoneB = new UserTimeZoneService(verifyB.DbContext, verifyB.CurrentUser, harness.Clock);
        (await timeZoneB.GetTimeZoneAsync()).Id.Should().Be("Pacific/Pago_Pago");
        (await timeZoneB.GetTodayAsync()).Should().Be(new DateOnly(2026, 10, 6));
        (await timeZoneB.IsDueTodayAsync(new DateOnly(2026, 10, 6))).Should().BeTrue();
        (await timeZoneB.IsOverdueAsync(new DateOnly(2026, 10, 7))).Should().BeFalse();
        var preferencesB = await new NotificationPreferencesService(verifyB.DbContext, verifyB.CurrentUser)
            .GetPreferencesAsync();
        preferencesB.Delivery.Should().Be(ReminderEmailDelivery.Immediate);
        preferencesB.BirthdayRemindersEnabled.Should().BeTrue();
        preferencesB.DefaultBirthdayLeadDays.Should().Be(2);
        (await new TwoFactorStatusService(verifyB.DbContext, verifyB.CurrentUser).GetStatusAsync())
            .Should().Be(new TwoFactorStatus(IsEnabled: false, RecoveryCodesLeft: 0));
    }

    private static ReminderService CreateReminderService(
        RelioDbContext dbContext,
        ICurrentUser currentUser,
        TimeProvider clock) =>
        new(dbContext, currentUser, new UserTimeZoneService(dbContext, currentUser, clock), clock);

    private static async Task<Guid> CreateInteractionAsync(
        RelioDbContext dbContext,
        SqlIsolationTestHarness harness,
        string ownerId,
        Guid personId,
        string description) =>
        await new InteractionService(dbContext, new FakeCurrentUser(ownerId), harness.Clock)
            .CreateAsync(new CreateInteractionRequest
            {
                ProfilePersonId = personId,
                OccurredOn = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime),
                Kind = InteractionKind.Call,
                Description = description,
                ParticipantIds = [personId],
            });

    private static Task RequiresUserAsync(Func<Task> action) =>
        Assert.ThrowsAsync<UnauthenticatedUserException>(action);
}
