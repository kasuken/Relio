using Microsoft.EntityFrameworkCore;
using Relio.Application.Accounts;
using Relio.Application.Security;

namespace Relio.Data.Identity;

/// <summary>
/// EF Core backed implementation of <see cref="ITwoFactorStatusService"/> (issue #20). Lives in
/// Relio.Data because it reads <see cref="RelioDbContext"/> directly, like every other service
/// implementation - see the "User-scoped data pattern" section of AGENTS.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>Untracked, on purpose.</b> This runs inside an interactive Blazor circuit, whose scoped
/// <c>DbContext</c> lives as long as the circuit. <c>UserManager.FindByIdAsync</c> and
/// <c>CountRecoveryCodesAsync</c> go through tracking queries (<c>FindAsync</c> returns whatever is
/// already tracked), so a circuit that had read the status once would keep reporting it after the
/// static account pages (a different request, a different context) changed it. Plain
/// <c>AsNoTracking</c> queries always see the database. Like every data service it runs in the
/// context's <see cref="Concurrency.DatabaseLane"/>, so it can load beside its sibling settings sections.
/// </para>
/// <para>
/// <b>Reading Identity's token rows.</b> Identity's user store keeps the recovery codes as one
/// <c>;</c>-joined string in <c>AspNetUserTokens</c> under a fixed provider and name. Those names
/// are private constants of <c>UserStoreBase</c>, mirrored below; a test proves
/// <c>UserManager.CountRecoveryCodesAsync</c> and this service always agree, so a change in a
/// future Identity release fails a test rather than silently reporting "0 codes left". Only the
/// <i>count</i> is read - never the codes themselves - and nothing here is logged.
/// </para>
/// </remarks>
public sealed class TwoFactorStatusService(RelioDbContext dbContext, ICurrentUser currentUser) : ITwoFactorStatusService
{
    // Mirrors UserStoreBase's private InternalLoginProvider / RecoveryCodeTokenName constants.
    internal const string IdentityTokenLoginProvider = "[AspNetUserStore]";
    internal const string RecoveryCodesTokenName = "RecoveryCodes";

    /// <inheritdoc />
    public async Task<TwoFactorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        var isEnabled = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.TwoFactorEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        if (isEnabled is not true)
        {
            // An unknown user (a valid cookie for a deleted account) is reported as "off", never as
            // an error: this only feeds a settings row.
            return new TwoFactorStatus(IsEnabled: false, RecoveryCodesLeft: 0);
        }

        var recoveryCodes = await dbContext.UserTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId
                && t.LoginProvider == IdentityTokenLoginProvider
                && t.Name == RecoveryCodesTokenName)
            .Select(t => t.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var left = string.IsNullOrEmpty(recoveryCodes) ? 0 : recoveryCodes.Split(';').Length;
        return new TwoFactorStatus(IsEnabled: true, left);
    }
}
