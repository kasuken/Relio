using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Data.Accounts;
using Relio.Data.Administration;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Data.Tests.Administration;
using Relio.Data.Tests.Infrastructure;
using Relio.Data.Tests.People;
using Relio.Data.Tests.Seeding;
using Relio.Domain;
using static Relio.Data.Tests.Administration.AdministrationTestHarness;

namespace Relio.Data.Tests.Accounts;

public sealed class AccountDeletionServiceTests
{
    [Fact]
    public async Task DeleteAsync_requires_confirmation_and_a_correct_password_without_writing()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var user = await CreateUserAsync(dbContext, "erase@example.com");
        dbContext.People.Add(new Person { OwnerId = user.Id, FirstName = "Private" });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, user.Id);

        (await service.DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: false)))
            .Status.Should().Be(AccountDeletionStatus.ConfirmationRequired);
        (await service.DeleteAsync(new AccountDeletionRequest("incorrect", Confirmed: true)))
            .Status.Should().Be(AccountDeletionStatus.CurrentPasswordIncorrect);

        (await dbContext.Users.AsNoTracking().AnyAsync(item => item.Id == user.Id)).Should().BeTrue();
        (await dbContext.People.AsNoTracking().CountAsync(item => item.OwnerId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_removes_every_protected_row_and_its_version_metadata()
    {
        var database = NewDatabase();
        string ownerId;
        await using (var seed = CreateDbContext(database))
        {
            var user = await CreateUserAsync(seed, "metadata-only@example.com");
            ownerId = user.Id;
            var person = new Person
            {
                OwnerId = ownerId,
                FirstName = "Private",
                HowWeMet = "Encrypted narrative",
                Details = "Encrypted details",
            };
            seed.People.Add(person);
            var note = new Note
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                Text = "Encrypted note",
            };
            seed.Notes.Add(note);
            var interaction = new Interaction
            {
                OwnerId = ownerId,
                OccurredOn = new DateOnly(2026, 10, 7),
                Kind = InteractionKind.Call,
                Description = "Encrypted interaction",
            };
            interaction.Participants.Add(new InteractionParticipant
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                InteractionId = interaction.Id,
            });
            seed.Interactions.Add(interaction);
            var reminder = new Reminder
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                Title = "Encrypted reminder",
                DueDate = new DateOnly(2026, 10, 7),
            };
            seed.Reminders.Add(reminder);
            var profile = new UserProfile
            {
                OwnerId = ownerId,
                UnsubscribeToken = "Encrypted unsubscribe token",
            };
            seed.UserProfiles.Add(profile);
            var identityToken = new IdentityUserToken<string>
            {
                UserId = ownerId,
                LoginProvider = "test",
                Name = "private",
                Value = "Encrypted Identity token",
            };
            seed.UserTokens.Add(identityToken);
            await seed.SaveChangesAsync();
            profile.UnsubscribeTokenVerifier.Should().NotBeNullOrEmpty();

            seed.Entry(person).Property<int>(FieldProtectionSchema.VersionPropertyName)
                .CurrentValue.Should().Be(FieldProtectionSchema.CurrentVersion);
            seed.Entry(profile).Property<int>(FieldProtectionSchema.VersionPropertyName)
                .CurrentValue.Should().Be(FieldProtectionSchema.CurrentVersion);
            seed.Entry(identityToken).Property<int>(FieldProtectionSchema.VersionPropertyName)
                .CurrentValue.Should().Be(FieldProtectionSchema.CurrentVersion);
            seed.Entry(interaction).Property<int>(FieldProtectionSchema.VersionPropertyName)
                .CurrentValue.Should().Be(FieldProtectionSchema.CurrentVersion);
            seed.Entry(note)
                .Property<int>(FieldProtectionSchema.VersionPropertyName)
                .CurrentValue
                .Should()
                .Be(FieldProtectionSchema.CurrentVersion);
            seed.Entry(reminder)
                .Property<int>(FieldProtectionSchema.VersionPropertyName)
                .CurrentValue
                .Should()
                .Be(FieldProtectionSchema.CurrentVersion);
        }

        await using var deletionContext = CreateDbContext(database);

        var result = await CreateService(deletionContext, ownerId)
            .DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.Deleted);
        await using var verify = CreateDbContext(database);
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == ownerId)).Should().BeFalse();
        (await verify.People.AsNoTracking().AnyAsync(person => person.OwnerId == ownerId)).Should().BeFalse();
        (await verify.Notes.AsNoTracking().AnyAsync(note => note.OwnerId == ownerId)).Should().BeFalse();
        (await verify.Interactions.AsNoTracking().AnyAsync(interaction => interaction.OwnerId == ownerId)).Should().BeFalse();
        (await verify.InteractionParticipants.AsNoTracking().AnyAsync(participant => participant.OwnerId == ownerId))
            .Should().BeFalse();
        (await verify.Reminders.AsNoTracking().AnyAsync(reminder => reminder.OwnerId == ownerId)).Should().BeFalse();
        (await verify.UserProfiles.AsNoTracking().AnyAsync(profile => profile.OwnerId == ownerId)).Should().BeFalse();
        (await verify.UserTokens.AsNoTracking().AnyAsync(token => token.UserId == ownerId)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_save_failure_leaves_the_complete_graph_and_clears_the_tracker()
    {
        var database = NewDatabase();
        string ownerId;
        await using (var seed = CreateDbContext(database))
        {
            var user = await CreateUserAsync(seed, "save-failure@example.com");
            ownerId = user.Id;
            var person = new Person { OwnerId = ownerId, FirstName = "Private" };
            seed.People.Add(person);
            seed.Notes.Add(new Note { OwnerId = ownerId, PersonId = person.Id, Text = "Private note" });
            await seed.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<RelioDbContext>(database)
            .AddInterceptors(new FailOnceSaveChangesInterceptor())
            .Options;
        await using var deletionContext = new RelioDbContext(
            options,
            TimeProvider.System,
            DataProtectionTestHarness.FieldProtector);
        deletionContext.Database.EnsureCreated();
        var delete = () => CreateService(deletionContext, ownerId)
            .DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: true));

        await delete.Should().ThrowAsync<InvalidOperationException>();
        deletionContext.ChangeTracker.Entries().Should().BeEmpty();

        await using var verify = CreateDbContext(database);
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == ownerId)).Should().BeTrue();
        (await verify.People.AsNoTracking().AnyAsync(person => person.OwnerId == ownerId)).Should().BeTrue();
        (await verify.Notes.AsNoTracking().AnyAsync(note => note.OwnerId == ownerId)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_refuses_to_remove_the_last_active_administrator()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var administrator = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateService(dbContext, administrator.Id);

        var result = await service.DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.LastActiveAdministrator);
        result.ConfirmationAddress.Should().BeNull();
        (await dbContext.Users.AsNoTracking().AnyAsync(item => item.Id == administrator.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_erases_only_the_current_users_full_account_graph()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var owner = await CreateUserAsync(dbContext, "owner@example.com");
        var other = await CreateUserAsync(dbContext, "other@example.com");
        var userManager = UserManagerTestFactory.Create(dbContext);

        var unrelatedRole = new IdentityRole { Name = "Operator", NormalizedName = "OPERATOR" };
        dbContext.Roles.Add(unrelatedRole);
        await dbContext.SaveChangesAsync();
        (await userManager.AddToRoleAsync(owner, unrelatedRole.Name!)).Succeeded.Should().BeTrue();

        (await userManager.AddClaimAsync(owner, new Claim("private-claim", "claim-value"))).Succeeded.Should().BeTrue();
        (await userManager.AddLoginAsync(owner, new UserLoginInfo("provider", "opaque-subject", "provider"))).Succeeded.Should().BeTrue();
        (await userManager.SetAuthenticationTokenAsync(owner, "provider", "private-token", "private-token-value")).Succeeded
            .Should().BeTrue();
        (await userManager.AddClaimAsync(other, new Claim("other-claim", "other-value"))).Succeeded.Should().BeTrue();

        var relationshipType = new RelationshipType { OwnerId = owner.Id, Name = "Friend", SortOrder = 0 };
        var tag = new Tag { OwnerId = owner.Id, Name = "Private tag" };
        var person = new Person
        {
            OwnerId = owner.Id,
            FirstName = "Private name",
            Details = "Encrypted private narrative",
            RelationshipTypeId = relationshipType.Id,
        };
        person.Tags.Add(tag);
        var interaction = new Interaction
        {
            OwnerId = owner.Id,
            Description = "Encrypted interaction narrative",
            Participants =
            {
                new InteractionParticipant
                {
                    OwnerId = owner.Id,
                    PersonId = person.Id,
                },
            },
        };
        interaction.Participants.Single().InteractionId = interaction.Id;

        dbContext.RelationshipTypes.Add(relationshipType);
        dbContext.Tags.Add(tag);
        dbContext.People.Add(person);
        dbContext.ContactMethods.Add(new ContactMethod
        {
            OwnerId = owner.Id,
            PersonId = person.Id,
            Value = "owner@example.com",
            NormalizedValue = "owner@example.com",
            Kind = ContactMethodKind.Email,
        });
        dbContext.Notes.Add(new Note
        {
            OwnerId = owner.Id,
            PersonId = person.Id,
            Text = "Encrypted private note",
        });
        dbContext.Reminders.Add(new Reminder
        {
            OwnerId = owner.Id,
            PersonId = person.Id,
            Title = "Encrypted reminder",
            DueDate = new DateOnly(2026, 10, 7),
        });
        dbContext.Interactions.Add(interaction);
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = owner.Id,
            TimeZoneId = "UTC",
            UnsubscribeToken = "private-unsubscribe-token",
        });
        dbContext.Set<ProductActivity>().Add(new ProductActivity
        {
            OwnerId = owner.Id,
            CohortStartedOnUtc = new DateOnly(2026, 10, 1),
            LastActiveOnUtc = new DateOnly(2026, 10, 2),
            RetentionExpiresAtUtc = new DateTime(2026, 12, 30, 0, 0, 0, DateTimeKind.Utc),
        });

        dbContext.People.Add(new Person { OwnerId = other.Id, FirstName = "Other user's person" });
        dbContext.Tags.Add(new Tag { OwnerId = other.Id, Name = "Other user's tag" });
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = other.Id, TimeZoneId = "UTC" });
        dbContext.Set<ProductActivity>().Add(new ProductActivity
        {
            OwnerId = other.Id,
            CohortStartedOnUtc = new DateOnly(2026, 10, 1),
            LastActiveOnUtc = new DateOnly(2026, 10, 2),
            RetentionExpiresAtUtc = new DateTime(2026, 12, 30, 0, 0, 0, DateTimeKind.Utc),
        });

        dbContext.RegistrationInvitations.AddRange(
            new RegistrationInvitation
            {
                Email = "another@example.com",
                NormalizedEmail = "ANOTHER@EXAMPLE.COM",
                TokenHash = new string('A', 64),
                CreatedByUserId = owner.Id,
            },
            new RegistrationInvitation
            {
                Email = owner.Email!,
                NormalizedEmail = owner.NormalizedEmail!,
                TokenHash = new string('B', 64),
                CreatedByUserId = other.Id,
            },
            new RegistrationInvitation
            {
                Email = "unrelated@example.com",
                NormalizedEmail = "UNRELATED@EXAMPLE.COM",
                TokenHash = new string('C', 64),
                CreatedByUserId = other.Id,
            });
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, owner.Id)
            .DeleteAsync(new AccountDeletionRequest(StrongPassword, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.Deleted);
        result.ConfirmationAddress.Should().Be(owner.Email);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await using var verify = CreateDbContext(database);
        (await verify.Users.AsNoTracking().AnyAsync(item => item.Id == owner.Id)).Should().BeFalse();
        (await verify.Users.AsNoTracking().AnyAsync(item => item.Id == other.Id)).Should().BeTrue();
        (await verify.Roles.AsNoTracking().AnyAsync(role => role.Id == unrelatedRole.Id)).Should().BeTrue();
        (await verify.UserRoles.AsNoTracking().AnyAsync(link => link.UserId == owner.Id)).Should().BeFalse();
        (await verify.UserClaims.AsNoTracking().AnyAsync(claim => claim.UserId == owner.Id)).Should().BeFalse();
        (await verify.UserLogins.AsNoTracking().AnyAsync(login => login.UserId == owner.Id)).Should().BeFalse();
        (await verify.UserTokens.AsNoTracking().AnyAsync(token => token.UserId == owner.Id)).Should().BeFalse();
        (await verify.UserClaims.AsNoTracking().AnyAsync(claim => claim.UserId == other.Id)).Should().BeTrue();

        (await verify.People.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.ContactMethods.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.Tags.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.RelationshipTypes.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.Interactions.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.InteractionParticipants.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.Notes.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.Reminders.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.UserProfiles.AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.Set<ProductActivity>().AsNoTracking().AnyAsync(item => item.OwnerId == owner.Id)).Should().BeFalse();
        (await verify.Set<Dictionary<string, object>>("PersonTag").AsNoTracking().CountAsync()).Should().Be(0);

        (await verify.People.AsNoTracking().CountAsync(item => item.OwnerId == other.Id)).Should().Be(1);
        (await verify.Tags.AsNoTracking().CountAsync(item => item.OwnerId == other.Id)).Should().Be(1);
        (await verify.UserProfiles.AsNoTracking().CountAsync(item => item.OwnerId == other.Id)).Should().Be(1);
        (await verify.Set<ProductActivity>().AsNoTracking().CountAsync(item => item.OwnerId == other.Id)).Should().Be(1);
        (await verify.RegistrationInvitations.AsNoTracking().Select(item => item.NormalizedEmail).ToListAsync())
            .Should().Equal("UNRELATED@EXAMPLE.COM");
    }

    [Fact]
    public async Task Concurrent_admin_erasures_leave_exactly_one_active_administrator()
    {
        var database = NewDatabase();
        await using var seed = CreateDbContext(database);
        var first = await CreateUserAsync(seed, "first-admin@example.com", administrator: true);
        var second = await CreateUserAsync(seed, "second-admin@example.com", administrator: true);
        await seed.SaveChangesAsync();
        var firstId = first.Id;
        var secondId = second.Id;

        var pause = new PauseSaveChangesInterceptor();
        await using var firstContext = CreateInterceptedContext(database, pause);
        await using var secondContext = CreateDbContext(database);
        var firstDelete = CreateService(firstContext, firstId).DeleteAsync(
            new AccountDeletionRequest(StrongPassword, Confirmed: true));
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondDelete = CreateService(secondContext, secondId).DeleteAsync(
            new AccountDeletionRequest(StrongPassword, Confirmed: true));
        secondDelete.IsCompleted.Should().BeFalse();
        pause.Release();

        var results = await Task.WhenAll(firstDelete, secondDelete);

        results.Count(result => result.Status == AccountDeletionStatus.Deleted).Should().Be(1);
        results.Count(result => result.Status == AccountDeletionStatus.LastActiveAdministrator).Should().Be(1);
        await using var verify = CreateDbContext(database);
        (await CountActiveAdministratorsAsync(verify)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_disable_and_erasure_leave_an_active_administrator()
    {
        var database = NewDatabase();
        await using var seed = CreateDbContext(database);
        var deletingAdministrator = await CreateUserAsync(seed, "delete-admin@example.com", administrator: true);
        var remainingAdministrator = await CreateUserAsync(seed, "remaining-admin@example.com", administrator: true);
        var deletingId = deletingAdministrator.Id;
        var remainingId = remainingAdministrator.Id;

        var pause = new PauseSaveChangesInterceptor();
        await using var administrationContext = CreateInterceptedContext(database, pause);
        await using var deletionContext = CreateDbContext(database);
        var disable = CreateAdministrationService(administrationContext, remainingId)
            .DisableAccountAsync(deletingId);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deletion = CreateService(deletionContext, deletingId).DeleteAsync(
            new AccountDeletionRequest(StrongPassword, Confirmed: true));
        deletion.IsCompleted.Should().BeFalse();
        pause.Release();

        var disableResult = await disable;
        var deletionResult = await deletion;

        deletionResult.Status.Should().Be(AccountDeletionStatus.AccountUnavailable);
        disableResult.Should().Be(AccountChangeResult.Succeeded);
        await using var verify = CreateDbContext(database);
        (await CountActiveAdministratorsAsync(verify)).Should().Be(1);
    }

    private static RelioDbContext CreateInterceptedContext(
        DbContextOptions<RelioDbContext> database,
        SaveChangesInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>(database)
            .AddInterceptors(interceptor)
            .Options;
        var dbContext = new RelioDbContext(
            options,
            TimeProvider.System,
            DataProtectionTestHarness.FieldProtector);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static AccountDeletionService CreateService(RelioDbContext dbContext, string? userId) =>
        new(
            dbContext,
            new FakeCurrentUser(userId),
            new PasswordHasher<RelioUser>(),
            NullLogger<AccountDeletionService>.Instance);

    private static Task<int> CountActiveAdministratorsAsync(RelioDbContext dbContext) =>
        (from link in dbContext.UserRoles.AsNoTracking()
         join role in dbContext.Roles.AsNoTracking() on link.RoleId equals role.Id
         join user in dbContext.Users.AsNoTracking() on link.UserId equals user.Id
         where role.Name == RelioRoles.Administrator && !user.IsDisabled
         select link.UserId).Distinct().CountAsync();

    private sealed class PauseSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }
}
