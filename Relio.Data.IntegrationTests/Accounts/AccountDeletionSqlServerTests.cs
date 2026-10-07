using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.Security;
using Relio.Data.Accounts;
using Relio.Data.Administration;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Accounts;

/// <summary>
/// Proves account erasure's transaction, owner-FK backstop, and database-wide lifecycle
/// serialization against the real SQL Server migrations.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AccountDeletionSqlServerTests : IAsyncLifetime
{
    private const string Password = "Str0ng-Passw0rd!";

    private string? _databaseConnectionString;

    public async Task InitializeAsync()
    {
        var serverConnectionString = SqlServerTestEnvironment.ServerConnectionString;
        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"Relio_AccountDeletion_{Guid.NewGuid():N}",
        };
        _databaseConnectionString = builder.ConnectionString;

        await using var dbContext = CreateDbContext();
        await SqlServerTestDatabase.CreateAndMigrateAsync(dbContext);
    }

    public async Task DisposeAsync()
    {
        if (_databaseConnectionString is null)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    private string ConnectionString => _databaseConnectionString
        ?? throw new InvalidOperationException(
            $"A SQL Server database is unavailable. Set {SqlServerTestEnvironment.ConnectionStringEnvironmentVariable}.");

    private RelioDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(interceptors)
            .Options;

        return new RelioDbContext(options, TimeProvider.System, FieldProtector);
    }

    [SqlServerFact]
    public async Task A_failure_after_person_deletes_rolls_back_the_entire_erasure()
    {
        string ownerId;
        Guid personId;
        await using (var seed = CreateDbContext())
        {
            var user = await AddAccountAsync(seed, administrator: false);
            ownerId = user.Id;
            var person = new Person { OwnerId = ownerId, FirstName = "Private" };
            seed.People.Add(person);
            personId = person.Id;
            seed.Notes.Add(new Note { OwnerId = ownerId, PersonId = personId, Text = "Private note" });
            await seed.SaveChangesAsync();
        }

        await using (var deleting = CreateDbContext(new ThrowAfterPeopleDeleteInterceptor()))
        {
            var deletion = CreateService(deleting, ownerId).DeleteAsync(
                new AccountDeletionRequest(Password, Confirmed: true));

            var failure = await Record.ExceptionAsync(() => deletion);
            failure.Should().NotBeNull();
        }

        await using var verify = CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == ownerId)).Should().BeTrue();
        (await verify.People.AsNoTracking().AnyAsync(person => person.Id == personId)).Should().BeTrue();
        (await verify.Notes.AsNoTracking().AnyAsync(note => note.OwnerId == ownerId)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task A_single_user_delete_still_uses_the_implicit_save_transaction()
    {
        string ownerId;
        await using (var seed = CreateDbContext())
        {
            ownerId = (await AddAccountAsync(seed, administrator: false)).Id;
        }

        var transactionObserver = new SingleUserDeleteTransactionInterceptor();
        await using var deleting = CreateDbContext(transactionObserver);
        var result = await CreateService(deleting, ownerId).DeleteAsync(
            new AccountDeletionRequest(Password, Confirmed: true));

        result.Status.Should().Be(AccountDeletionStatus.Deleted);
        transactionObserver.SawUserDelete.Should().BeTrue();
        transactionObserver.UserDeleteUsedTransaction.Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Erasure_deletes_protected_rows_without_decrypting_private_values()
    {
        string ownerId;
        await using (var seed = CreateDbContext())
        {
            ownerId = (await AddAccountAsync(seed, administrator: false)).Id;
            var person = new Person
            {
                OwnerId = ownerId,
                FirstName = "Encrypted first name",
                HowWeMet = "Encrypted narrative",
                Details = "Encrypted details",
            };
            seed.People.Add(person);
            seed.Notes.Add(new Note
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                Text = "Encrypted note",
            });
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
            seed.Reminders.Add(new Reminder
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                Title = "Encrypted reminder",
                DueDate = new DateOnly(2026, 10, 7),
            });
            seed.UserProfiles.Add(new UserProfile
            {
                OwnerId = ownerId,
                UnsubscribeToken = "Encrypted unsubscribe token",
            });
            seed.UserTokens.Add(new IdentityUserToken<string>
            {
                UserId = ownerId,
                LoginProvider = "test",
                Name = "private",
                Value = "Encrypted Identity token",
            });
            await seed.SaveChangesAsync();
        }

        await using (var deleting = CreateContextWithProtector(new ThrowingUnprotector()))
        {
            var result = await CreateService(deleting, ownerId).DeleteAsync(
                new AccountDeletionRequest(Password, Confirmed: true));
            result.Status.Should().Be(AccountDeletionStatus.Deleted);
        }

        await using var verify = CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == ownerId)).Should().BeFalse();
        (await verify.People.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await verify.Notes.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await verify.Interactions.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await verify.InteractionParticipants.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId))
            .Should().BeFalse();
        (await verify.Reminders.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await verify.UserProfiles.AsNoTracking().AnyAsync(item => item.OwnerId == ownerId)).Should().BeFalse();
        (await verify.UserTokens.AsNoTracking().AnyAsync(item => item.UserId == ownerId)).Should().BeFalse();
    }

    [SqlServerFact]
    public async Task A_late_owned_row_forces_the_erasure_transaction_to_roll_back_for_retry()
    {
        string ownerId;
        Guid originalPersonId;
        await using (var seed = CreateDbContext())
        {
            var user = await AddAccountAsync(seed, administrator: false);
            ownerId = user.Id;
            var originalPerson = new Person { OwnerId = ownerId, FirstName = "Original" };
            seed.People.Add(originalPerson);
            originalPersonId = originalPerson.Id;
            await seed.SaveChangesAsync();
        }

        var interceptor = new InsertLatePersonOnSaveInterceptor(() => CreateDbContext(), ownerId);
        await using (var deleting = CreateDbContext(interceptor))
        {
            var result = await CreateService(deleting, ownerId).DeleteAsync(
                new AccountDeletionRequest(Password, Confirmed: true));

            result.Status.Should().Be(AccountDeletionStatus.ConcurrentChange);
        }

        await using var verify = CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == ownerId)).Should().BeTrue();
        (await verify.People.AsNoTracking().AnyAsync(person => person.Id == originalPersonId)).Should().BeTrue();
        (await verify.People.AsNoTracking().AnyAsync(
            person => person.OwnerId == ownerId && person.FirstName == "Late write")).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task The_owner_foreign_key_rejects_a_stale_session_write_after_erasure()
    {
        string ownerId;
        await using (var seed = CreateDbContext())
        {
            ownerId = (await AddAccountAsync(seed, administrator: false)).Id;
        }

        await using (var deleting = CreateDbContext())
        {
            var result = await CreateService(deleting, ownerId).DeleteAsync(
                new AccountDeletionRequest(Password, Confirmed: true));
            result.Status.Should().Be(AccountDeletionStatus.Deleted);
        }

        await using var staleWriter = CreateDbContext();
        staleWriter.People.Add(new Person { OwnerId = ownerId, FirstName = "Stale writer" });
        var failure = await Record.ExceptionAsync(() => staleWriter.SaveChangesAsync());

        failure.Should().BeOfType<DbUpdateException>()
            .Which.InnerException.Should().BeOfType<SqlException>()
            .Which.Number.Should().Be(547);
    }

    [SqlServerFact]
    public async Task Erasure_removes_only_the_current_owner_graph_and_keeps_shared_schema_data()
    {
        string erasedOwnerId;
        string survivingOwnerId;
        Guid erasedPersonId;
        Guid survivingPersonId;
        string globalRoleId;
        string erasedEmail;
        string survivingInvitationEmail;
        await using (var seed = CreateDbContext())
        {
            var owner = await AddAccountAsync(seed, administrator: false);
            var other = await AddAccountAsync(seed, administrator: false);
            erasedOwnerId = owner.Id;
            survivingOwnerId = other.Id;
            erasedEmail = owner.Email!;
            survivingInvitationEmail = $"other-invitation-{Guid.NewGuid():N}@example.com";

            var globalRoleName = $"Unrelated-{Guid.NewGuid():N}";
            var globalRole = new IdentityRole
            {
                Name = globalRoleName,
                NormalizedName = globalRoleName.ToUpperInvariant(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
            };
            seed.Roles.Add(globalRole);
            globalRoleId = globalRole.Id;

            var erasedType = new RelationshipType { OwnerId = erasedOwnerId, Name = "Friend" };
            var erasedTag = new Tag { OwnerId = erasedOwnerId, Name = "Erased tag" };
            var erasedPerson = new Person
            {
                OwnerId = erasedOwnerId,
                FirstName = "Erased",
                RelationshipTypeId = erasedType.Id,
            };
            erasedPerson.Tags.Add(erasedTag);
            erasedPersonId = erasedPerson.Id;
            var erasedInteraction = new Interaction
            {
                OwnerId = erasedOwnerId,
                Description = "Encrypted interaction",
            };
            erasedInteraction.Participants.Add(new InteractionParticipant
            {
                OwnerId = erasedOwnerId,
                PersonId = erasedPersonId,
                InteractionId = erasedInteraction.Id,
            });

            var survivingTag = new Tag { OwnerId = survivingOwnerId, Name = "Surviving tag" };
            var survivingPerson = new Person { OwnerId = survivingOwnerId, FirstName = "Surviving" };
            survivingPerson.Tags.Add(survivingTag);
            survivingPersonId = survivingPerson.Id;

            seed.RelationshipTypes.Add(erasedType);
            seed.Tags.AddRange(erasedTag, survivingTag);
            seed.People.AddRange(erasedPerson, survivingPerson);
            seed.ContactMethods.Add(new ContactMethod
            {
                OwnerId = erasedOwnerId,
                PersonId = erasedPersonId,
                Kind = ContactMethodKind.Email,
                Value = "erased@example.com",
                NormalizedValue = "erased@example.com",
            });
            seed.Notes.Add(new Note
            {
                OwnerId = erasedOwnerId,
                PersonId = erasedPersonId,
                Text = "Encrypted note",
            });
            seed.Reminders.Add(new Reminder
            {
                OwnerId = erasedOwnerId,
                PersonId = erasedPersonId,
                Title = "Encrypted reminder",
                DueDate = new DateOnly(2026, 10, 7),
            });
            seed.Interactions.Add(erasedInteraction);
            seed.UserProfiles.Add(new UserProfile { OwnerId = erasedOwnerId, TimeZoneId = "UTC" });
            seed.UserProfiles.Add(new UserProfile { OwnerId = survivingOwnerId, TimeZoneId = "UTC" });
            seed.Set<ProductActivity>().Add(new ProductActivity
            {
                OwnerId = erasedOwnerId,
                CohortStartedOnUtc = new DateOnly(2026, 10, 1),
                LastActiveOnUtc = new DateOnly(2026, 10, 2),
                RetentionExpiresAtUtc = new DateTime(2026, 12, 30, 0, 0, 0, DateTimeKind.Utc),
            });
            seed.Set<ProductActivity>().Add(new ProductActivity
            {
                OwnerId = survivingOwnerId,
                CohortStartedOnUtc = new DateOnly(2026, 10, 1),
                LastActiveOnUtc = new DateOnly(2026, 10, 2),
                RetentionExpiresAtUtc = new DateTime(2026, 12, 30, 0, 0, 0, DateTimeKind.Utc),
            });
            seed.RegistrationInvitations.AddRange(
                NewInvitation(erasedOwnerId, erasedEmail),
                NewInvitation(survivingOwnerId, survivingInvitationEmail));
            await seed.SaveChangesAsync();
        }

        await using (var deleting = CreateDbContext())
        {
            var result = await CreateService(deleting, erasedOwnerId).DeleteAsync(
                new AccountDeletionRequest(Password, Confirmed: true));
            result.Status.Should().Be(AccountDeletionStatus.Deleted);
        }

        await using var verify = CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == erasedOwnerId)).Should().BeFalse();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == survivingOwnerId)).Should().BeTrue();
        (await verify.Roles.AsNoTracking().AnyAsync(role => role.Id == globalRoleId)).Should().BeTrue();
        (await verify.People.AsNoTracking().AnyAsync(person => person.Id == erasedPersonId)).Should().BeFalse();
        (await verify.People.AsNoTracking().AnyAsync(person => person.Id == survivingPersonId)).Should().BeTrue();
        (await verify.ContactMethods.AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.Tags.AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.RelationshipTypes.AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.Interactions.AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.InteractionParticipants.AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.Notes.AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.Reminders.AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.UserProfiles.AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.Set<ProductActivity>().AsNoTracking().AnyAsync(item => item.OwnerId == erasedOwnerId)).Should().BeFalse();
        (await verify.Set<Dictionary<string, object>>("PersonTag").AsNoTracking().CountAsync()).Should().Be(1);
        (await verify.RegistrationInvitations.AsNoTracking()
            .AnyAsync(item => item.CreatedByUserId == erasedOwnerId
                || item.NormalizedEmail == erasedEmail.ToUpperInvariant())).Should().BeFalse();
        (await verify.RegistrationInvitations.AsNoTracking()
            .AnyAsync(item => item.NormalizedEmail == survivingInvitationEmail.ToUpperInvariant())).Should().BeTrue();
        (await verify.Tags.AsNoTracking().AnyAsync(item => item.OwnerId == survivingOwnerId)).Should().BeTrue();
        (await verify.Set<ProductActivity>().AsNoTracking().AnyAsync(item => item.OwnerId == survivingOwnerId)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task Two_admin_erasures_are_serialized_and_preserve_one_active_administrator()
    {
        string firstId;
        string secondId;
        await using (var seed = CreateDbContext())
        {
            firstId = (await AddAccountAsync(seed, administrator: true)).Id;
            secondId = (await AddAccountAsync(seed, administrator: true)).Id;
        }

        var pause = new PauseSaveChangesInterceptor();
        await using var firstContext = CreateDbContext(pause);
        await using var secondContext = CreateDbContext();
        var firstDelete = CreateService(firstContext, firstId).DeleteAsync(
            new AccountDeletionRequest(Password, Confirmed: true));
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondDelete = CreateService(secondContext, secondId).DeleteAsync(
            new AccountDeletionRequest(Password, Confirmed: true));
        secondDelete.IsCompleted.Should().BeFalse();
        pause.Release();

        var results = await Task.WhenAll(firstDelete, secondDelete);

        results.Count(result => result.Status == AccountDeletionStatus.Deleted).Should().Be(1);
        results.Count(result => result.Status == AccountDeletionStatus.LastActiveAdministrator).Should().Be(1);
        await using var verify = CreateDbContext();
        (await CountActiveAdministratorsAsync(verify)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task An_admin_disable_racing_erasure_cannot_disable_the_remaining_administrator()
    {
        string deletingId;
        string remainingId;
        await using (var seed = CreateDbContext())
        {
            deletingId = (await AddAccountAsync(seed, administrator: true)).Id;
            remainingId = (await AddAccountAsync(seed, administrator: true)).Id;
        }

        var pause = new PauseSaveChangesInterceptor();
        await using var adminContext = CreateDbContext(pause);
        await using var deletingContext = CreateDbContext();
        using var identityProvider = CreateIdentityProvider(adminContext);
        using var identityScope = identityProvider.CreateScope();
        var administration = new UserAdministrationService(
            adminContext,
            identityScope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>(),
            new TestCurrentUser(remainingId),
            TimeProvider.System,
            Options.Create(new RegistrationOptions { Mode = RegistrationMode.Open }),
            NullLogger<UserAdministrationService>.Instance);

        var disable = administration.DisableAccountAsync(deletingId);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deletion = CreateService(deletingContext, deletingId).DeleteAsync(
            new AccountDeletionRequest(Password, Confirmed: true));
        deletion.IsCompleted.Should().BeFalse();
        pause.Release();

        (await disable).Should().Be(AccountChangeResult.Succeeded);
        (await deletion).Status.Should().Be(AccountDeletionStatus.AccountUnavailable);
        await using var verify = CreateDbContext();
        (await CountActiveAdministratorsAsync(verify)).Should().Be(1);
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == remainingId && !user.IsDisabled))
            .Should().BeTrue();
    }

    [SqlServerFact]
    public async Task A_stale_erased_administrator_cannot_disable_the_remaining_administrator()
    {
        string erasedAdministratorId;
        string remainingAdministratorId;
        await using (var seed = CreateDbContext())
        {
            erasedAdministratorId = (await AddAccountAsync(seed, administrator: true)).Id;
            remainingAdministratorId = (await AddAccountAsync(seed, administrator: true)).Id;
        }

        await using (var deleting = CreateDbContext())
        {
            var result = await CreateService(deleting, erasedAdministratorId).DeleteAsync(
                new AccountDeletionRequest(Password, Confirmed: true));
            result.Status.Should().Be(AccountDeletionStatus.Deleted);
        }

        await using var staleAdministratorContext = CreateDbContext();
        using var identityProvider = CreateIdentityProvider(staleAdministratorContext);
        using var identityScope = identityProvider.CreateScope();
        var administration = new UserAdministrationService(
            staleAdministratorContext,
            identityScope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>(),
            new TestCurrentUser(erasedAdministratorId),
            TimeProvider.System,
            Options.Create(new RegistrationOptions { Mode = RegistrationMode.Open }),
            NullLogger<UserAdministrationService>.Instance);

        var act = () => administration.DisableAccountAsync(remainingAdministratorId);

        await act.Should().ThrowAsync<AdministratorRequiredException>();
        (await staleAdministratorContext.Users.AsNoTracking()
            .AnyAsync(user => user.Id == remainingAdministratorId && !user.IsDisabled)).Should().BeTrue();
        (await CountActiveAdministratorsAsync(staleAdministratorContext)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task A_database_lock_from_an_independent_session_blocks_lifecycle_reads()
    {
        string ownerId;
        await using (var seed = CreateDbContext())
        {
            ownerId = (await AddAccountAsync(seed, administrator: true)).Id;
        }

        await using var externalSession = new SqlConnection(ConnectionString);
        await externalSession.OpenAsync();
        await using (var acquire = externalSession.CreateCommand())
        {
            acquire.CommandText =
                "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
                "@Resource = N'Relio.AccountLifecycle', @LockMode = 'Exclusive', " +
                "@LockOwner = 'Session', @LockTimeout = 15000; SELECT @result;";
            Convert.ToInt32(await acquire.ExecuteScalarAsync(), CultureInfo.InvariantCulture)
                .Should().BeGreaterThanOrEqualTo(0);
        }

        var connectionOpened = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionObserver = new SignalOnOpenConnectionInterceptor(connectionOpened);
        await using var deletionContext = CreateDbContext(connectionObserver);
        var deletion = CreateService(deletionContext, ownerId).DeleteAsync(
            new AccountDeletionRequest(Password, Confirmed: true));
        var lifecycleSessionId = await connectionOpened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForApplicationLockWaitAsync(lifecycleSessionId);
        deletion.IsCompleted.Should().BeFalse();

        await using (var release = externalSession.CreateCommand())
        {
            release.CommandText =
                "DECLARE @result int; EXEC @result = sys.sp_releaseapplock " +
                "@Resource = N'Relio.AccountLifecycle', @LockOwner = 'Session'; SELECT @result;";
            Convert.ToInt32(await release.ExecuteScalarAsync(), CultureInfo.InvariantCulture)
                .Should().BeGreaterThanOrEqualTo(0);
        }

        (await deletion).Status.Should().Be(AccountDeletionStatus.LastActiveAdministrator);
    }

    private async Task WaitForApplicationLockWaitAsync(int sessionId)
    {
        await using var observer = new SqlConnection(ConnectionString);
        await observer.OpenAsync();
        await using var command = observer.CreateCommand();
        command.CommandText =
            "SELECT CASE WHEN EXISTS (" +
            "SELECT 1 FROM sys.dm_exec_requests AS request " +
            "JOIN sys.dm_tran_locks AS applicationLock " +
            "ON applicationLock.request_session_id = request.session_id " +
            "WHERE request.session_id = @sessionId " +
            "AND request.wait_type LIKE N'LCK_M_%' " +
            "AND applicationLock.resource_type = N'APPLICATION' " +
            "AND applicationLock.request_status = N'WAIT' " +
            "AND applicationLock.resource_database_id = DB_ID()" +
            ") THEN 1 ELSE 0 END;";
        command.CommandTimeout = 10;
        command.Parameters.AddWithValue("@sessionId", sessionId);

        var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(10);
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            var isWaiting = Convert.ToInt32(
                await command.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture);
            if (isWaiting == 1)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException("The account lifecycle lock was not observed waiting in SQL Server.");
    }

    private static async Task<RelioUser> AddAccountAsync(RelioDbContext dbContext, bool administrator)
    {
        var email = $"erase-{Guid.NewGuid():N}@example.com";
        var user = new RelioUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
        user.PasswordHash = new PasswordHasher<RelioUser>().HashPassword(user, Password);
        dbContext.Users.Add(user);

        if (administrator)
        {
            var roleId = await dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == RelioRoles.Administrator)
                .Select(role => role.Id)
                .SingleAsync();
            dbContext.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId });
        }

        await dbContext.SaveChangesAsync();
        return user;
    }

    private static RegistrationInvitation NewInvitation(string createdByUserId, string email) => new()
    {
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        TokenHash = InvitationTokens.Hash(InvitationTokens.Generate()),
        CreatedByUserId = createdByUserId,
        CreatedAtUtc = TimeProvider.System.GetUtcNow().UtcDateTime,
        ExpiresAtUtc = TimeProvider.System.GetUtcNow().UtcDateTime.AddDays(7),
    };

    private static AccountDeletionService CreateService(RelioDbContext dbContext, string ownerId) =>
        new(
            dbContext,
            new TestCurrentUser(ownerId),
            new PasswordHasher<RelioUser>(),
            NullLogger<AccountDeletionService>.Instance);

    private RelioDbContext CreateContextWithProtector(IDataProtectionFieldProtector protector)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new RelioDbContext(options, TimeProvider.System, protector);
    }

    private static Task<int> CountActiveAdministratorsAsync(RelioDbContext dbContext) =>
        (from link in dbContext.UserRoles.AsNoTracking()
         join role in dbContext.Roles.AsNoTracking() on link.RoleId equals role.Id
         join user in dbContext.Users.AsNoTracking() on link.UserId equals user.Id
         where role.Name == RelioRoles.Administrator && !user.IsDisabled
         select link.UserId).Distinct().CountAsync();

    private static ServiceProvider CreateIdentityProvider(RelioDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<RelioUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<RelioDbContext>();
        services.AddScoped(_ => dbContext);
        return services.BuildServiceProvider();
    }

    private sealed class TestCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }

    private sealed class ThrowingUnprotector : IDataProtectionFieldProtector
    {
        public Guid ModelCacheIdentity { get; } = Guid.NewGuid();

        public string? Protect(string? value, string purpose) => value;

        public string? Unprotect(string? protectedValue, string purpose) =>
            throw new InvalidOperationException("Private content must not be materialized during deletion.");
    }

    private sealed class ThrowAfterPeopleDeleteInterceptor : DbCommandInterceptor
    {
        private int _injected;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            InjectFailureAfterPeopleDelete(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            InjectFailureAfterPeopleDelete(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            InjectFailureAfterPeopleDelete(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            InjectFailureAfterPeopleDelete(command);
            return ValueTask.FromResult(result);
        }

        private void InjectFailureAfterPeopleDelete(DbCommand command)
        {
            if (command.CommandText.Contains("DELETE FROM [People]", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _injected, 1) == 0)
            {
                command.CommandText +=
                    "; THROW 51000, 'Injected account-erasure transaction failure', 1;";
            }
        }
    }

    private sealed class SingleUserDeleteTransactionInterceptor : DbCommandInterceptor
    {
        public bool SawUserDelete { get; private set; }

        public bool UserDeleteUsedTransaction { get; private set; }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Observe(command, eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Observe(command, eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            if (!command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains("[AspNetUsers]", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SawUserDelete = true;
            UserDeleteUsedTransaction = eventData.Context?.Database.CurrentTransaction is not null;
        }
    }

    private sealed class InsertLatePersonOnSaveInterceptor(
        Func<RelioDbContext> createDbContext,
        string ownerId) : SaveChangesInterceptor
    {
        private int _inserted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _inserted, 1) == 0)
            {
                await using var writer = createDbContext();
                writer.People.Add(new Person { OwnerId = ownerId, FirstName = "Late write" });
                await writer.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class PauseSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    private sealed class SignalOnOpenConnectionInterceptor(TaskCompletionSource<int> signal) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT @@SPID;";
            var sessionId = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            signal.TrySetResult(sessionId);
        }
    }
}
