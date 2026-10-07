using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.Security;
using Relio.Data.Administration;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Accounts;

/// <summary>Erases the authenticated account and its complete owner-scoped data graph (#59).</summary>
public sealed class AccountDeletionService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    IPasswordHasher<RelioUser> passwordHasher,
    ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
    /// <inheritdoc />
    public async Task<AccountDeletionResult> DeleteAsync(
        AccountDeletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        ArgumentNullException.ThrowIfNull(request);

        dbContext.ChangeTracker.Clear();
        try
        {
            if (!request.Confirmed)
            {
                return new AccountDeletionResult(AccountDeletionStatus.ConfirmationRequired);
            }

            await using var lifecycleScope = await AccountLifecycleGate.EnterAsync(dbContext, cancellationToken);

            // Read only the account metadata needed to reauthenticate, check its state, and send a
            // post-commit confirmation. No private profile/content is selected.
            var account = await dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == ownerId)
                .Select(user => new AccountReauthenticationData(
                    user.Id,
                    user.Email,
                    user.NormalizedEmail,
                    user.PasswordHash,
                    user.IsDisabled,
                    user.ConcurrencyStamp))
                .SingleOrDefaultAsync(cancellationToken);

            if (account is null || account.IsDisabled || string.IsNullOrEmpty(account.PasswordHash))
            {
                return new AccountDeletionResult(AccountDeletionStatus.AccountUnavailable);
            }

            var passwordUser = new RelioUser { Id = ownerId, PasswordHash = account.PasswordHash };
            var verification = passwordHasher.VerifyHashedPassword(
                passwordUser,
                account.PasswordHash,
                request.CurrentPassword ?? string.Empty);
            if (verification == PasswordVerificationResult.Failed)
            {
                return new AccountDeletionResult(AccountDeletionStatus.CurrentPasswordIncorrect);
            }

            if (await IsActiveAdministratorAsync(ownerId, cancellationToken)
                && await CountActiveAdministratorsAsync(cancellationToken) <= 1)
            {
                return new AccountDeletionResult(AccountDeletionStatus.LastActiveAdministrator);
            }

            var personIds = await dbContext.People
                .AsNoTracking()
                .Where(person => person.OwnerId == ownerId)
                .Select(person => person.Id)
                .ToListAsync(cancellationToken);
            var tagIds = await dbContext.Tags
                .AsNoTracking()
                .Where(tag => tag.OwnerId == ownerId)
                .Select(tag => tag.Id)
                .ToListAsync(cancellationToken);

            await RemovePersonTagLinksAsync(personIds, tagIds, cancellationToken);

            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.InteractionParticipants.Where(item => item.OwnerId == ownerId),
                id => new InteractionParticipant { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.ContactMethods.Where(item => item.OwnerId == ownerId),
                id => new ContactMethod { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.Notes.Where(item => item.OwnerId == ownerId),
                id => new Note { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.Reminders.Where(item => item.OwnerId == ownerId),
                id => new Reminder { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.Interactions.Where(item => item.OwnerId == ownerId),
                id => new Interaction { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.People.Where(item => item.OwnerId == ownerId),
                id => new Person { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.Tags.Where(item => item.OwnerId == ownerId),
                id => new Tag { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.RelationshipTypes.Where(item => item.OwnerId == ownerId),
                id => new RelationshipType { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.UserProfiles.Where(item => item.OwnerId == ownerId),
                id => new UserProfile { Id = id, OwnerId = ownerId },
                cancellationToken);
            await RemoveOwnedRowsAsync(
                dbContext,
                dbContext.Set<ProductActivity>().Where(item => item.OwnerId == ownerId),
                id => new ProductActivity { Id = id, OwnerId = ownerId },
                cancellationToken);

            await RemoveInvitationsAsync(ownerId, account.NormalizedEmail, cancellationToken);
            await RemoveIdentityDependentsAsync(ownerId, cancellationToken);
            MarkUserForDeletion(ownerId, account.ConcurrencyStamp);

            // Keep this as one tracked save, and force EF's implicit transaction even when this
            // account happens to produce only one command (EF normally skips that transaction).
            // A late owned-row insert is rejected by the OwnerId -> AspNetUsers NO ACTION foreign
            // key; that rolls the entire batch back and is surfaced as ConcurrentChange below.
            var previousTransactionBehavior = dbContext.Database.AutoTransactionBehavior;
            dbContext.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                dbContext.Database.AutoTransactionBehavior = previousTransactionBehavior;
            }

            logger.LogInformation("User {UserId} deleted their account and owned data.", ownerId);
            return new AccountDeletionResult(AccountDeletionStatus.Deleted, account.Email);
        }
        catch (AccountLifecycleConcurrencyException)
        {
            return new AccountDeletionResult(AccountDeletionStatus.ConcurrentChange);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new AccountDeletionResult(AccountDeletionStatus.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            return new AccountDeletionResult(AccountDeletionStatus.ConcurrentChange);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task RemovePersonTagLinksAsync(
        IReadOnlyCollection<Guid> personIds,
        IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0 && tagIds.Count == 0)
        {
            return;
        }

        var links = dbContext.Set<Dictionary<string, object>>("PersonTag");
        var rows = await links
            .AsNoTracking()
            .Where(link =>
                personIds.Contains(EF.Property<Guid>(link, "PeopleId"))
                || tagIds.Contains(EF.Property<Guid>(link, "TagsId")))
            .ToListAsync(cancellationToken);
        links.RemoveRange(rows);
    }

    private async Task RemoveInvitationsAsync(
        string ownerId,
        string? normalizedEmail,
        CancellationToken cancellationToken)
    {
        var invitations = string.IsNullOrEmpty(normalizedEmail)
            ? dbContext.RegistrationInvitations.Where(invitation => invitation.CreatedByUserId == ownerId)
            : dbContext.RegistrationInvitations.Where(
                invitation => invitation.CreatedByUserId == ownerId
                    || invitation.NormalizedEmail == normalizedEmail);

        var ids = await invitations
            .AsNoTracking()
            .Select(invitation => invitation.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            var invitation = new RegistrationInvitation { Id = id };
            dbContext.Attach(invitation);
            dbContext.Entry(invitation).State = EntityState.Deleted;
        }
    }

    private async Task RemoveIdentityDependentsAsync(string userId, CancellationToken cancellationToken)
    {
        var claimIds = await dbContext.UserClaims
            .AsNoTracking()
            .Where(claim => claim.UserId == userId)
            .Select(claim => claim.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in claimIds)
        {
            MarkDeleted(new IdentityUserClaim<string> { Id = id });
        }

        var logins = await dbContext.UserLogins
            .AsNoTracking()
            .Where(login => login.UserId == userId)
            .Select(login => new { login.LoginProvider, login.ProviderKey })
            .ToListAsync(cancellationToken);
        foreach (var login in logins)
        {
            var loginProvider = login.LoginProvider
                ?? throw new InvalidOperationException("Identity login provider must not be null.");
            var providerKey = login.ProviderKey
                ?? throw new InvalidOperationException("Identity provider key must not be null.");
            MarkDeleted(new IdentityUserLogin<string>
            {
                UserId = userId,
                LoginProvider = loginProvider,
                ProviderKey = providerKey,
            });
        }

        var tokenEntityType = dbContext.Model.FindEntityType(typeof(IdentityUserToken<string>));
        if (tokenEntityType?.FindProperty(FieldProtectionSchema.VersionPropertyName) is null)
        {
            throw new InvalidOperationException(
                "Identity user-token rows must include the sensitive-data protection version.");
        }

        var protectedTokens = await dbContext.UserTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId)
            .Select(token => new TokenKeyVersion(
                token.LoginProvider,
                token.Name,
                EF.Property<int>(token, FieldProtectionSchema.VersionPropertyName)))
            .ToListAsync(cancellationToken);
        foreach (var token in protectedTokens)
        {
            var loginProvider = token.LoginProvider
                ?? throw new InvalidOperationException("Identity user-token login provider must not be null.");
            var name = token.Name
                ?? throw new InvalidOperationException("Identity user-token name must not be null.");
            var entity = new IdentityUserToken<string>
            {
                UserId = userId,
                LoginProvider = loginProvider,
                Name = name,
            };
            dbContext.Attach(entity);
            var version = dbContext.Entry(entity).Property<int>(FieldProtectionSchema.VersionPropertyName);
            version.CurrentValue = token.Version;
            version.OriginalValue = token.Version;
            dbContext.Entry(entity).State = EntityState.Deleted;
        }

        var roleIds = await dbContext.UserRoles
            .AsNoTracking()
            .Where(role => role.UserId == userId)
            .Select(role => role.RoleId)
            .ToListAsync(cancellationToken);
        foreach (var roleId in roleIds)
        {
            MarkDeleted(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
        }
    }

    private void MarkUserForDeletion(string userId, string? concurrencyStamp)
    {
        var trackedUser = dbContext.ChangeTracker
            .Entries<RelioUser>()
            .FirstOrDefault(entry => string.Equals(entry.Entity.Id, userId, StringComparison.Ordinal));
        if (trackedUser is not null)
        {
            trackedUser.State = EntityState.Deleted;
            return;
        }

        var user = new RelioUser { Id = userId, ConcurrencyStamp = concurrencyStamp };
        dbContext.Attach(user);
        dbContext.Entry(user).State = EntityState.Deleted;
    }

    private void MarkDeleted<TEntity>(TEntity entity)
        where TEntity : class
    {
        dbContext.Attach(entity);
        dbContext.Entry(entity).State = EntityState.Deleted;
    }

    private async Task<bool> IsActiveAdministratorAsync(string userId, CancellationToken cancellationToken)
    {
        var roleId = await dbContext.Roles
            .AsNoTracking()
            .Where(role => role.Name == RelioRoles.Administrator)
            .Select(role => role.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (roleId is null)
        {
            return false;
        }

        return await dbContext.UserRoles
            .AsNoTracking()
            .AnyAsync(
                userRole => userRole.UserId == userId && userRole.RoleId == roleId,
                cancellationToken);
    }

    private async Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken)
    {
        var activeAdministratorIds =
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            join user in dbContext.Users.AsNoTracking() on userRole.UserId equals user.Id
            where role.Name == RelioRoles.Administrator && !user.IsDisabled
            select userRole.UserId;

        return await activeAdministratorIds.Distinct().CountAsync(cancellationToken);
    }

    private static async Task RemoveOwnedRowsAsync<TEntity>(
        RelioDbContext dbContext,
        IQueryable<TEntity> ownerRows,
        Func<Guid, TEntity> createStub,
        CancellationToken cancellationToken)
        where TEntity : class, IOwnedEntity
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException("The erased entity must be mapped.");
        var hasProtectionVersion = entityType.FindProperty(FieldProtectionSchema.VersionPropertyName) is not null;
        var foreignKeys = entityType.GetForeignKeys()
            .SelectMany(key => key.Properties)
            .Where(property => property.Name != nameof(IOwnedEntity.OwnerId))
            .Distinct()
            .ToArray();
        var item = Expression.Parameter(typeof(TEntity), "item");
        var id = Expression.Property(item, nameof(IOwnedEntity.Id));
        Expression version = hasProtectionVersion
            ? Expression.Convert(
                Expression.Call(typeof(EF), nameof(EF.Property), [typeof(int)],
                    item, Expression.Constant(FieldProtectionSchema.VersionPropertyName)),
                typeof(int?))
            : Expression.Constant(null, typeof(int?));
        var foreignKeyValues = Expression.NewArrayInit(
            typeof(object),
            foreignKeys.Select(property => Expression.Convert(
                Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType],
                    item, Expression.Constant(property.Name)),
                typeof(object))));
        var constructor = typeof(OwnedDeletionRow).GetConstructor([typeof(Guid), typeof(int?), typeof(object[])])
            ?? throw new InvalidOperationException("The erasure metadata projection is unavailable.");
        var projection = Expression.Lambda<Func<TEntity, OwnedDeletionRow>>(
            Expression.New(constructor, id, version, foreignKeyValues), item);
        var rows = await ownerRows.AsNoTracking().Select(projection).ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var stub = createStub(row.Id);
            var entry = dbContext.Entry(stub);
            // EF needs the real foreign keys to order deletes before database cascades; an
            // id-only stub could let a cascade remove a row before its explicit DELETE runs.
            for (var index = 0; index < foreignKeys.Length; index++)
            {
                entry.Property(foreignKeys[index].Name).CurrentValue = row.ForeignKeyValues[index];
            }

            dbContext.Attach(stub);
            if (row.ProtectionVersion is int protectionVersion)
            {
                var protection = entry.Property<int>(FieldProtectionSchema.VersionPropertyName);
                protection.CurrentValue = protectionVersion;
                protection.OriginalValue = protectionVersion;
            }

            entry.State = EntityState.Deleted;
        }
    }

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 547 };

    private sealed record AccountReauthenticationData(
        string Id,
        string? Email,
        string? NormalizedEmail,
        string? PasswordHash,
        bool IsDisabled,
        string? ConcurrencyStamp);

    private sealed record OwnedDeletionRow(Guid Id, int? ProtectionVersion, object?[] ForeignKeyValues);

    private sealed record TokenKeyVersion(string? LoginProvider, string? Name, int Version);
}
