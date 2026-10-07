using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.Security;
using Relio.Data.Accounts;
using Relio.Data.Administration;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class AccountDeletionSqlIsolationTests : IAsyncLifetime
{
    private readonly SqlServerDatabaseFixture _isolatedFixture = new();

    public Task InitializeAsync() => _isolatedFixture.InitializeAsync();

    public Task DisposeAsync() => _isolatedFixture.DisposeAsync();

    [SqlServerFact]
    public async Task Anonymous_call_is_rejected_before_account_data_is_accessed()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(_isolatedFixture);
        await using var anonymousScope = harness.As(null);
        var service = CreateService(anonymousScope);

        var exception = await Assert.ThrowsAsync<UnauthenticatedUserException>(
            () => service.DeleteAsync(new AccountDeletionRequest(
                SqlIsolationTestHarness.TestPassword,
                Confirmed: true)));

        exception.Should().NotBeNull();
        await using var verify = _isolatedFixture.CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeTrue();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Confirmation_password_and_active_account_checks_reject_without_erasing_owned_data()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(_isolatedFixture);
        var personId = await SeedPersonAsync(harness.OwnerA.Id);
        await using var ownerScope = harness.AsOwnerA();
        var service = CreateService(ownerScope);

        var confirmation = await service.DeleteAsync(new AccountDeletionRequest(
            SqlIsolationTestHarness.TestPassword,
            Confirmed: false));
        var wrongPassword = await service.DeleteAsync(new AccountDeletionRequest(
            "not-the-password",
            Confirmed: true));

        confirmation.Status.Should().Be(AccountDeletionStatus.ConfirmationRequired);
        wrongPassword.Status.Should().Be(AccountDeletionStatus.CurrentPasswordIncorrect);
        confirmation.ConfirmationAddress.Should().BeNull();
        wrongPassword.ConfirmationAddress.Should().BeNull();

        await using (var disable = _isolatedFixture.CreateDbContext())
        {
            var user = await disable.Users.SingleAsync(candidate => candidate.Id == harness.OwnerA.Id);
            user.IsDisabled = true;
            await disable.SaveChangesAsync();
        }

        var disabledAccount = await service.DeleteAsync(new AccountDeletionRequest(
            SqlIsolationTestHarness.TestPassword,
            Confirmed: true));
        var removedAccount = await harness.CreateAdditionalAccountAsync();
        await using var removedScope = harness.As(removedAccount.Id);
        var removedService = CreateService(removedScope);
        var firstRemoval = await removedService.DeleteAsync(new AccountDeletionRequest(
            SqlIsolationTestHarness.TestPassword,
            Confirmed: true));
        var missingAccount = await removedService.DeleteAsync(new AccountDeletionRequest(
            SqlIsolationTestHarness.TestPassword,
            Confirmed: true));

        disabledAccount.Should().Be(new AccountDeletionResult(AccountDeletionStatus.AccountUnavailable));
        firstRemoval.Status.Should().Be(AccountDeletionStatus.Deleted);
        missingAccount.Should().Be(disabledAccount);
        await using var verify = _isolatedFixture.CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeTrue();
        (await verify.People.AsNoTracking().CountAsync(person => person.Id == personId)).Should().Be(1);
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Last_active_administrator_is_protected_and_can_delete_after_another_admin_exists()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(_isolatedFixture, makeOwnerAAdministrator: true);
        var personId = await SeedPersonAsync(harness.OwnerA.Id);
        await using (var ownerScope = harness.AsOwnerA())
        {
            var result = await CreateService(ownerScope).DeleteAsync(new AccountDeletionRequest(
                SqlIsolationTestHarness.TestPassword,
                Confirmed: true));
            result.Status.Should().Be(AccountDeletionStatus.LastActiveAdministrator);
            result.ConfirmationAddress.Should().BeNull();
        }

        await using (var verify = _isolatedFixture.CreateDbContext())
        {
            (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeTrue();
            (await verify.People.AsNoTracking().AnyAsync(person => person.Id == personId)).Should().BeTrue();
        }

        var otherAdministrator = await harness.CreateAdditionalAccountAsync(isAdministrator: true);
        await using var deleteScope = harness.AsOwnerA();
        var deleted = await CreateService(deleteScope).DeleteAsync(new AccountDeletionRequest(
            SqlIsolationTestHarness.TestPassword,
            Confirmed: true));

        deleted.Status.Should().Be(AccountDeletionStatus.Deleted);
        deleted.ConfirmationAddress.Should().Be(harness.OwnerA.Email);
        await using var finalCheck = _isolatedFixture.CreateDbContext();
        (await finalCheck.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeFalse();
        (await finalCheck.Users.AsNoTracking().AnyAsync(user => user.Id == otherAdministrator.Id)).Should().BeTrue();
        (await finalCheck.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Erasure_removes_the_identity_owners_full_sql_graph_and_preserves_another_owner()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(_isolatedFixture, makeOwnerAAdministrator: true);
        var remainingAdministrator = await harness.CreateAdditionalAccountAsync(isAdministrator: true);
        var graph = await SeedFullGraphAsync(harness);

        AccountDeletionResult result;
        await using (var ownerScope = harness.AsOwnerA())
        {
            result = await CreateService(ownerScope).DeleteAsync(new AccountDeletionRequest(
                SqlIsolationTestHarness.TestPassword,
                Confirmed: true));
            ownerScope.DbContext.ChangeTracker.Entries().Should().BeEmpty();
        }

        result.Status.Should().Be(AccountDeletionStatus.Deleted);
        result.ConfirmationAddress.Should().Be(harness.OwnerA.Email);

        await using var verify = _isolatedFixture.CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == remainingAdministrator.Id)).Should().BeTrue();
        (await verify.UserRoles.AsNoTracking().AnyAsync(link => link.UserId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.UserRoles.AsNoTracking().AnyAsync(link => link.UserId == remainingAdministrator.Id)).Should().BeTrue();
        (await verify.UserClaims.AsNoTracking().AnyAsync(claim => claim.UserId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.UserLogins.AsNoTracking().AnyAsync(login => login.UserId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.UserTokens.AsNoTracking().AnyAsync(token => token.UserId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.UserClaims.AsNoTracking().AnyAsync(claim => claim.UserId == harness.OwnerB.Id)).Should().BeTrue();
        (await verify.UserTokens.AsNoTracking().AnyAsync(token => token.UserId == harness.OwnerB.Id)).Should().BeTrue();

        (await verify.People.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.ContactMethods.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.Tags.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.RelationshipTypes.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.Interactions.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.InteractionParticipants.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.Notes.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.Reminders.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.UserProfiles.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.RelationshipTypes.AsNoTracking().AnyAsync(item => item.OwnerId == harness.OwnerB.Id)).Should().BeTrue();
        (await verify.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(item => item.OwnerId == harness.OwnerA.Id)).Should().BeFalse();

        (await verify.People.AsNoTracking().AnyAsync(item => item.Id == graph.OwnerBPersonId)).Should().BeTrue();
        (await verify.Tags.AsNoTracking().AnyAsync(item => item.Id == graph.OwnerBTagId)).Should().BeTrue();
        (await verify.ContactMethods.AsNoTracking()
            .AnyAsync(item => item.Id == graph.OwnerBContactMethodId)).Should().BeTrue();
        (await verify.Interactions.AsNoTracking()
            .AnyAsync(item => item.Id == graph.OwnerBInteractionId)).Should().BeTrue();
        (await verify.InteractionParticipants.AsNoTracking()
            .AnyAsync(item => item.Id == graph.OwnerBParticipantId)).Should().BeTrue();
        (await verify.Notes.AsNoTracking().AnyAsync(item => item.Id == graph.OwnerBNoteId)).Should().BeTrue();
        (await verify.Reminders.AsNoTracking()
            .AnyAsync(item => item.Id == graph.OwnerBReminderId)).Should().BeTrue();
        (await verify.UserProfiles.AsNoTracking()
            .AnyAsync(item => item.OwnerId == harness.OwnerB.Id)).Should().BeTrue();
        (await verify.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(item => item.OwnerId == harness.OwnerB.Id)).Should().BeTrue();

        var links = verify.Set<Dictionary<string, object>>("PersonTag").AsNoTracking();
        (await links.AnyAsync(link => EF.Property<Guid>(link, "PeopleId") == graph.OwnerAPersonId
            || EF.Property<Guid>(link, "TagsId") == graph.OwnerATagId)).Should().BeFalse();
        (await links.AnyAsync(link => EF.Property<Guid>(link, "PeopleId") == graph.OwnerBPersonId
            && EF.Property<Guid>(link, "TagsId") == graph.OwnerBTagId)).Should().BeTrue();

        (await verify.RegistrationInvitations.AsNoTracking()
            .AnyAsync(invitation => invitation.Id == graph.OwnerAInvitationId)).Should().BeFalse();
        (await verify.RegistrationInvitations.AsNoTracking()
            .AnyAsync(invitation => invitation.Id == graph.InvitationForOwnerAEmailId)).Should().BeFalse();
        (await verify.RegistrationInvitations.AsNoTracking()
            .AnyAsync(invitation => invitation.Id == graph.UnrelatedOwnerBInvitationId)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Late_owner_row_rejects_erasure_and_rolls_back_the_entire_sql_write_batch()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(_isolatedFixture);
        var originalPersonId = await SeedPersonAsync(harness.OwnerA.Id);
        await using (var seed = _isolatedFixture.CreateDbContext())
        {
            seed.Notes.Add(new Note
            {
                OwnerId = harness.OwnerA.Id,
                PersonId = originalPersonId,
                Text = "Synthetic rollback note",
            });
            await seed.SaveChangesAsync();
        }

        var lateInsert = new LateOwnerPersonInsertInterceptor(_isolatedFixture, harness.OwnerA.Id);
        await using var deleteContext = _isolatedFixture.CreateDbContext(lateInsert);
        var result = await CreateService(deleteContext, harness.OwnerA.Id).DeleteAsync(
            new AccountDeletionRequest(SqlIsolationTestHarness.TestPassword, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.ConcurrentChange);
        lateInsert.InsertedPersonId.Should().NotBe(Guid.Empty);
        await using var verify = _isolatedFixture.CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeTrue();
        (await verify.People.AsNoTracking().CountAsync(person => person.OwnerId == harness.OwnerA.Id)).Should().Be(2);
        (await verify.Notes.AsNoTracking().CountAsync(note => note.OwnerId == harness.OwnerA.Id)).Should().Be(1);
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Erasure_holds_the_database_wide_lifecycle_lock_on_its_sql_session_through_save()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(_isolatedFixture);
        var lockProbe = new AccountLifecycleLockProbeInterceptor(_isolatedFixture);
        await using var deleteContext = _isolatedFixture.CreateDbContext(lockProbe);

        var result = await CreateService(deleteContext, harness.OwnerA.Id).DeleteAsync(
            new AccountDeletionRequest(SqlIsolationTestHarness.TestPassword, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.Deleted);
        lockProbe.ObservedCompetingLockTimeout.Should().BeTrue();
        await using var verify = _isolatedFixture.CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Concurrent_administrator_deletions_leave_one_active_identity_administrator()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(_isolatedFixture, makeOwnerAAdministrator: true);
        var otherAdministrator = await harness.CreateAdditionalAccountAsync(isAdministrator: true);
        await using var ownerAContext = _isolatedFixture.CreateDbContext();
        await using var otherAdminContext = _isolatedFixture.CreateDbContext();
        var ownerADelete = CreateService(ownerAContext, harness.OwnerA.Id).DeleteAsync(
            new AccountDeletionRequest(SqlIsolationTestHarness.TestPassword, Confirmed: true));
        var otherAdminDelete = CreateService(otherAdminContext, otherAdministrator.Id).DeleteAsync(
            new AccountDeletionRequest(SqlIsolationTestHarness.TestPassword, Confirmed: true));

        var outcomes = await Task.WhenAll(ownerADelete, otherAdminDelete);

        outcomes.Count(outcome => outcome.Status == AccountDeletionStatus.Deleted).Should().Be(1);
        outcomes.Count(outcome => outcome.Status == AccountDeletionStatus.LastActiveAdministrator).Should().Be(1);
        await using var verify = _isolatedFixture.CreateDbContext();
        var survivingAdministrators = await (
            from link in verify.UserRoles.AsNoTracking()
            join role in verify.Roles.AsNoTracking() on link.RoleId equals role.Id
            join user in verify.Users.AsNoTracking() on link.UserId equals user.Id
            where (link.UserId == harness.OwnerA.Id || link.UserId == otherAdministrator.Id)
                && role.Name == RelioRoles.Administrator
                && !user.IsDisabled
            select link.UserId).Distinct().ToListAsync();
        survivingAdministrators.Should().ContainSingle();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerB.Id)).Should().BeTrue();
    }

    private async Task<Guid> SeedPersonAsync(string ownerId)
    {
        await using var dbContext = _isolatedFixture.CreateDbContext();
        return await TestDataFactory.CreatePersonAsync(
            dbContext,
            ownerId,
            $"Synthetic-{Guid.NewGuid():N}");
    }

    private async Task<SeededOwnerGraphs> SeedFullGraphAsync(SqlIsolationTestHarness harness)
    {
        await using var dbContext = _isolatedFixture.CreateDbContext();
        using var identityServices = SqlIsolationTestHarness.CreateIdentityServices(dbContext);
        var userManager = identityServices.GetRequiredService<UserManager<RelioUser>>();
        var ownerA = await userManager.FindByIdAsync(harness.OwnerA.Id)
            ?? throw new InvalidOperationException("A synthetic Identity account was not found.");
        var ownerB = await userManager.FindByIdAsync(harness.OwnerB.Id)
            ?? throw new InvalidOperationException("A synthetic Identity account was not found.");

        (await userManager.AddClaimAsync(ownerA, new Claim("isolation", "owner-a"))).Succeeded.Should().BeTrue();
        (await userManager.AddLoginAsync(
            ownerA,
            new UserLoginInfo("isolation-provider", $"subject-{ownerA.Id}", "Synthetic"))).Succeeded.Should().BeTrue();
        (await userManager.SetAuthenticationTokenAsync(
            ownerA,
            "isolation-provider",
            "synthetic-token",
            "owner-a-token")).Succeeded.Should().BeTrue();
        (await userManager.AddClaimAsync(ownerB, new Claim("isolation", "owner-b"))).Succeeded.Should().BeTrue();
        (await userManager.SetAuthenticationTokenAsync(
            ownerB,
            "isolation-provider",
            "synthetic-token",
            "owner-b-token")).Succeeded.Should().BeTrue();

        var relationshipTypeA = await dbContext.RelationshipTypes.AsNoTracking()
            .Where(type => type.OwnerId == ownerA.Id)
            .OrderBy(type => type.SortOrder)
            .FirstAsync();
        var relationshipTypeB = await dbContext.RelationshipTypes.AsNoTracking()
            .Where(type => type.OwnerId == ownerB.Id)
            .OrderBy(type => type.SortOrder)
            .FirstAsync();

        var tagA = new Tag { OwnerId = ownerA.Id, Name = $"Isolation-A-{Guid.NewGuid():N}" };
        var tagB = new Tag { OwnerId = ownerB.Id, Name = $"Isolation-B-{Guid.NewGuid():N}" };
        var personA = new Person
        {
            OwnerId = ownerA.Id,
            FirstName = "Synthetic A",
            RelationshipTypeId = relationshipTypeA.Id,
            Details = "Synthetic protected profile detail.",
        };
        var personB = new Person
        {
            OwnerId = ownerB.Id,
            FirstName = "Synthetic B",
            RelationshipTypeId = relationshipTypeB.Id,
            Details = "Synthetic protected profile detail.",
        };
        personA.Tags.Add(tagA);
        personB.Tags.Add(tagB);

        var interactionA = new Interaction
        {
            OwnerId = ownerA.Id,
            OccurredOn = new DateOnly(2026, 10, 5),
            Description = "Synthetic protected interaction.",
        };
        var participantA = new InteractionParticipant
        {
            OwnerId = ownerA.Id,
            InteractionId = interactionA.Id,
            PersonId = personA.Id,
        };
        interactionA.Participants.Add(participantA);

        var interactionB = new Interaction
        {
            OwnerId = ownerB.Id,
            OccurredOn = new DateOnly(2026, 10, 6),
            Description = "Synthetic protected interaction.",
        };
        var participantB = new InteractionParticipant
        {
            OwnerId = ownerB.Id,
            InteractionId = interactionB.Id,
            PersonId = personB.Id,
        };
        interactionB.Participants.Add(participantB);

        var profileA = await dbContext.UserProfiles.SingleAsync(profile => profile.OwnerId == ownerA.Id);
        profileA.UnsubscribeToken = $"synthetic-unsubscribe-{Guid.NewGuid():N}";

        var profileB = await dbContext.UserProfiles.SingleAsync(profile => profile.OwnerId == ownerB.Id);
        profileB.UnsubscribeToken = $"synthetic-unsubscribe-{Guid.NewGuid():N}";

        var invitationEmail = $"invitee-{Guid.NewGuid():N}@example.com";
        var invitationForOwnerA = new RegistrationInvitation
        {
            Email = invitationEmail,
            NormalizedEmail = invitationEmail.ToUpperInvariant(),
            TokenHash = new string('A', 64),
            CreatedByUserId = ownerA.Id,
            CreatedAtUtc = harness.Clock.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = harness.Clock.GetUtcNow().AddDays(7).UtcDateTime,
        };
        var invitationForOwnerAEmail = new RegistrationInvitation
        {
            Email = harness.OwnerA.Email,
            NormalizedEmail = harness.OwnerA.Email.ToUpperInvariant(),
            TokenHash = new string('B', 64),
            CreatedByUserId = ownerB.Id,
            CreatedAtUtc = harness.Clock.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = harness.Clock.GetUtcNow().AddDays(7).UtcDateTime,
        };
        var unrelatedInvitation = new RegistrationInvitation
        {
            Email = $"unrelated-{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"UNRELATED-{Guid.NewGuid():N}@EXAMPLE.COM",
            TokenHash = new string('C', 64),
            CreatedByUserId = ownerB.Id,
            CreatedAtUtc = harness.Clock.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = harness.Clock.GetUtcNow().AddDays(7).UtcDateTime,
        };

        dbContext.Tags.AddRange(tagA, tagB);
        dbContext.People.AddRange(personA, personB);
        dbContext.ContactMethods.AddRange(
            CreateContact(ownerA.Id, personA.Id, "owner-a@example.com"),
            CreateContact(ownerB.Id, personB.Id, "owner-b@example.com"));
        dbContext.Interactions.AddRange(interactionA, interactionB);
        dbContext.InteractionParticipants.AddRange(participantA, participantB);
        dbContext.Notes.AddRange(
            new Note { OwnerId = ownerA.Id, PersonId = personA.Id, Text = "Synthetic protected note A." },
            new Note { OwnerId = ownerB.Id, PersonId = personB.Id, Text = "Synthetic protected note B." });
        dbContext.Reminders.AddRange(
            new Reminder
            {
                OwnerId = ownerA.Id,
                PersonId = personA.Id,
                Title = "Synthetic reminder A",
                DueDate = new DateOnly(2026, 10, 8),
            },
            new Reminder
            {
                OwnerId = ownerB.Id,
                PersonId = personB.Id,
                Title = "Synthetic reminder B",
                DueDate = new DateOnly(2026, 10, 9),
            });
        dbContext.Set<ProductActivity>().AddRange(
            CreateProductActivity(ownerA.Id, harness.Clock),
            CreateProductActivity(ownerB.Id, harness.Clock));
        dbContext.RegistrationInvitations.AddRange(
            invitationForOwnerA,
            invitationForOwnerAEmail,
            unrelatedInvitation);
        await dbContext.SaveChangesAsync();

        return new SeededOwnerGraphs(
            personA.Id,
            personB.Id,
            tagA.Id,
            tagB.Id,
            interactionB.Id,
            participantB.Id,
            dbContext.ContactMethods.Local.Single(method => method.OwnerId == ownerB.Id).Id,
            dbContext.Notes.Local.Single(note => note.OwnerId == ownerB.Id).Id,
            dbContext.Reminders.Local.Single(reminder => reminder.OwnerId == ownerB.Id).Id,
            invitationForOwnerA.Id,
            invitationForOwnerAEmail.Id,
            unrelatedInvitation.Id);
    }

    private static ContactMethod CreateContact(string ownerId, Guid personId, string email) => new()
    {
        OwnerId = ownerId,
        PersonId = personId,
        Kind = ContactMethodKind.Email,
        Value = email,
        NormalizedValue = email,
    };

    private static ProductActivity CreateProductActivity(string ownerId, TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return new ProductActivity
        {
            OwnerId = ownerId,
            CohortStartedOnUtc = today,
            LastActiveOnUtc = today,
            ReturnedInDays30To59 = false,
            RetentionExpiresAtUtc = clock.GetUtcNow().AddDays(90).UtcDateTime,
        };
    }

    private static AccountDeletionService CreateService(SqlIsolationScope scope) =>
        CreateService(scope.DbContext, scope.CurrentUser.UserId);

    private static AccountDeletionService CreateService(RelioDbContext dbContext, string? userId) =>
        new(
            dbContext,
            new FakeCurrentUser(userId),
            new PasswordHasher<RelioUser>(),
            NullLogger<AccountDeletionService>.Instance);

    private sealed record SeededOwnerGraphs(
        Guid OwnerAPersonId,
        Guid OwnerBPersonId,
        Guid OwnerATagId,
        Guid OwnerBTagId,
        Guid OwnerBInteractionId,
        Guid OwnerBParticipantId,
        Guid OwnerBContactMethodId,
        Guid OwnerBNoteId,
        Guid OwnerBReminderId,
        Guid OwnerAInvitationId,
        Guid InvitationForOwnerAEmailId,
        Guid UnrelatedOwnerBInvitationId);

    private sealed class LateOwnerPersonInsertInterceptor(
        SqlServerDatabaseFixture fixture,
        string ownerId) : SaveChangesInterceptor
    {
        private int _inserted;

        public Guid InsertedPersonId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _inserted, 1) == 0)
            {
                await using var competingContext = fixture.CreateDbContext();
                var person = new Person { OwnerId = ownerId, FirstName = "Synthetic late row" };
                competingContext.People.Add(person);
                await competingContext.SaveChangesAsync(cancellationToken);
                InsertedPersonId = person.Id;
            }

            return result;
        }
    }

    private sealed class AccountLifecycleLockProbeInterceptor(SqlServerDatabaseFixture fixture)
        : SaveChangesInterceptor
    {
        private int _observed;

        public bool ObservedCompetingLockTimeout { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _observed, 1) == 0)
            {
                await using var connection = new SqlConnection(fixture.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "DECLARE @result int; " +
                    "EXEC @result = sys.sp_getapplock " +
                    "@Resource = N'Relio.AccountLifecycle', @LockMode = 'Exclusive', " +
                    "@LockOwner = 'Session', @LockTimeout = 0; SELECT @result;";
                var lockResult = Convert.ToInt32(
                    await command.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture);
                ObservedCompetingLockTimeout = lockResult == -1;
                if (lockResult >= 0)
                {
                    command.CommandText =
                        "EXEC sys.sp_releaseapplock " +
                        "@Resource = N'Relio.AccountLifecycle', @LockOwner = 'Session';";
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            return result;
        }
    }
}
