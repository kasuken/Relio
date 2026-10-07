using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Application.Security;
using Relio.Data.Accounts;
using Relio.Data.Identity;

namespace Relio.Data.Administration;

/// <summary>
/// EF Core and Identity backed implementation of <see cref="IUserAdministrationService"/>
/// (issue #19). Lives in Relio.Data because it depends on <see cref="RelioDbContext"/> and
/// <see cref="UserManager{TUser}"/> directly.
/// </summary>
/// <remarks>
/// <para>
/// Every method starts with <see cref="RequireAdministratorAsync"/>, which re-reads the caller from
/// the database: the role and the disabled flag are not trusted from claims, which only refresh at
/// sign-in or security stamp validation.
/// </para>
/// <para>
/// This class touches only <c>AspNetUsers</c>, role membership and <c>RegistrationInvitations</c>.
/// It never queries <c>People</c>, <c>Tags</c> or <c>UserProfiles</c> - not even to show a display
/// name - and a test proves running every operation leaves another user's owned rows untouched.
/// It logs ids only, never emails, tokens or links.
/// </para>
/// </remarks>
public sealed class UserAdministrationService(
    RelioDbContext dbContext,
    UserManager<RelioUser> userManager,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<RegistrationOptions> options,
    ILogger<UserAdministrationService> logger) : IUserAdministrationService
{
    private const int EmailMaxLength = 256;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        var administratorId = await RequireAdministratorAsync();

        var administratorIds = (await userManager.GetUsersInRoleAsync(RelioRoles.Administrator))
            .Select(u => u.Id)
            .ToHashSet(StringComparer.Ordinal);
        var now = timeProvider.GetUtcNow();

        var users = await dbContext.Users
            .AsNoTracking()
            .OrderBy(u => u.NormalizedEmail)
            .ToListAsync(cancellationToken);

        return users
            .Select(u => new AccountSummary(
                u.Id,
                u.Email ?? u.UserName ?? string.Empty,
                u.EmailConfirmed,
                administratorIds.Contains(u.Id),
                u.IsDisabled,
                u.LockoutEnd is { } lockoutEnd && lockoutEnd > now,
                u.Id == administratorId))
            .ToList();
    }

    /// <inheritdoc />
    public Task<AccountChangeResult> DisableAccountAsync(string userId, CancellationToken cancellationToken = default) =>
        SetDisabledAsync(userId, disabled: true, cancellationToken);

    /// <inheritdoc />
    public Task<AccountChangeResult> EnableAccountAsync(string userId, CancellationToken cancellationToken = default) =>
        SetDisabledAsync(userId, disabled: false, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvitationSummary>> ListPendingInvitationsAsync(
        CancellationToken cancellationToken = default)
    {
        await RequireAdministratorAsync();
        await PurgeExpiredInvitationsAsync(cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await dbContext.RegistrationInvitations
            .AsNoTracking()
            .Where(i => i.ExpiresAtUtc > now)
            .OrderBy(i => i.ExpiresAtUtc)
            .Select(i => new InvitationSummary(i.Id, i.Email, i.CreatedAtUtc, i.ExpiresAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CreateInvitationResult> CreateInvitationAsync(
        string email, CancellationToken cancellationToken = default)
    {
        await using var lifecycleScope = await AccountLifecycleGate.EnterAsync(dbContext, cancellationToken);
        var administratorId = await RequireAdministratorAsync();

        if (options.Value.Mode != RegistrationMode.InviteOnly)
        {
            return new CreateInvitationResult(CreateInvitationStatus.InvitationsNotInUse, null);
        }

        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Length > EmailMaxLength
            || !new EmailAddressAttribute().IsValid(trimmed))
        {
            return new CreateInvitationResult(CreateInvitationStatus.InvalidEmail, null);
        }

        if (await userManager.FindByEmailAsync(trimmed) is not null)
        {
            return new CreateInvitationResult(CreateInvitationStatus.AlreadyHasAccount, null);
        }

        await PurgeExpiredInvitationsAsync(cancellationToken);

        var normalizedEmail = userManager.NormalizeEmail(trimmed);

        // A newer invitation for the same address replaces the older one, whose link stops working.
        var replaced = await dbContext.RegistrationInvitations
            .Where(i => i.NormalizedEmail == normalizedEmail)
            .ToListAsync(cancellationToken);
        dbContext.RegistrationInvitations.RemoveRange(replaced);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = InvitationTokens.Generate();
        var invitation = new RegistrationInvitation
        {
            Email = trimmed,
            NormalizedEmail = normalizedEmail,
            TokenHash = InvitationTokens.Hash(token),
            CreatedByUserId = administratorId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + options.Value.InvitationLifetime,
        };
        dbContext.RegistrationInvitations.Add(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Administrator {AdministratorId} created invitation {InvitationId}.", administratorId, invitation.Id);

        return new CreateInvitationResult(
            CreateInvitationStatus.Created,
            new CreatedInvitation(invitation.Id, invitation.Email, token, invitation.ExpiresAtUtc));
    }

    /// <inheritdoc />
    public async Task<bool> RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        var administratorId = await RequireAdministratorAsync();

        var invitation = await dbContext.RegistrationInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null)
        {
            return false;
        }

        dbContext.RegistrationInvitations.Remove(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Administrator {AdministratorId} revoked invitation {InvitationId}.", administratorId, invitationId);
        return true;
    }

    private async Task<AccountChangeResult> SetDisabledAsync(
        string userId,
        bool disabled,
        CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();
        await using var lifecycleScope = await AccountLifecycleGate.EnterAsync(dbContext, cancellationToken);
        var administratorId = await RequireAdministratorAsync();

        if (string.Equals(administratorId, userId, StringComparison.Ordinal))
        {
            return AccountChangeResult.CannotChangeOwnAccount;
        }

        var user = string.IsNullOrEmpty(userId) ? null : await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AccountChangeResult.NotFound;
        }

        // FindByIdAsync returns a copy this context already tracks without asking the database, and
        // a circuit's context is long-lived: reload, or a change made elsewhere since (a failed
        // sign-in bumps the concurrency stamp) would make the save below fail.
        await dbContext.Entry(user).ReloadAsync(cancellationToken);
        if (dbContext.Entry(user).State == EntityState.Detached)
        {
            return AccountChangeResult.NotFound;
        }

        if (user.IsDisabled == disabled)
        {
            return AccountChangeResult.Succeeded;
        }

        if (disabled
            && await IsAdministratorAccountAsync(user.Id, cancellationToken)
            && await CountActiveAdministratorsAsync(cancellationToken) <= 1)
        {
            return AccountChangeResult.LastActiveAdministrator;
        }

        // The flag is set first: UpdateSecurityStampAsync saves the whole user, so the flag and the
        // new stamp reach the database together. Rotating the stamp is what ends open sessions
        // (cookie validation and circuit revalidation both compare it) - see AGENTS.md.
        user.IsDisabled = disabled;
        var result = disabled
            ? await userManager.UpdateSecurityStampAsync(user)
            : await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Changing account {user.Id} failed: {string.Join("; ", result.Errors.Select(e => e.Code))}");
        }

        logger.LogInformation(
            "Administrator {AdministratorId} {Action} account {UserId}.",
            administratorId,
            disabled ? "disabled" : "enabled",
            user.Id);
        return AccountChangeResult.Succeeded;
    }

    private async Task PurgeExpiredInvitationsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expired = await dbContext.RegistrationInvitations
            .Where(i => i.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        // Loaded and removed (not ExecuteDelete): ExecuteDelete is not supported by the InMemory
        // provider the unit tests use, and an instance has a handful of invitations at most.
        dbContext.RegistrationInvitations.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsAdministratorAccountAsync(string userId, CancellationToken cancellationToken)
    {
        var administratorRoleId = await dbContext.Roles
            .AsNoTracking()
            .Where(role => role.Name == RelioRoles.Administrator)
            .Select(role => role.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return administratorRoleId is not null
            && await dbContext.UserRoles
                .AsNoTracking()
                .AnyAsync(
                    userRole => userRole.UserId == userId && userRole.RoleId == administratorRoleId,
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

    private async Task<string> RequireAdministratorAsync()
    {
        var userId = currentUser.RequireUserId();

        // Two plain, untracked queries - deliberately not UserManager.FindByIdAsync/IsInRoleAsync,
        // which resolve through the DbContext's change tracker. In an interactive circuit this
        // context lives as long as the circuit, so a tracked copy loaded earlier would keep
        // answering "yes, active Administrator" after the account was disabled or demoted.
        var isDisabled = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.IsDisabled)
            .FirstOrDefaultAsync();

        var isAdministrator = isDisabled == false
            && await (from userRole in dbContext.UserRoles.AsNoTracking()
                      join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                      where userRole.UserId == userId && role.Name == RelioRoles.Administrator
                      select userRole).AnyAsync();

        if (!isAdministrator)
        {
            logger.LogWarning("User {UserId} attempted an administrator operation without being an active Administrator.", userId);
            throw new AdministratorRequiredException();
        }

        return userId;
    }
}
