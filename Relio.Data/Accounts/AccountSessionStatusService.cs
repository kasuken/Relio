using Microsoft.EntityFrameworkCore;
using Relio.Application.Accounts;
using Relio.Application.Security;

namespace Relio.Data.Accounts;

/// <summary>Returns a fresh account-existence/disabled check for circuit and request boundaries.</summary>
public sealed class AccountSessionStatusService(
    RelioDbContext dbContext,
    ICurrentUser currentUser) : IAccountSessionStatusService
{
    /// <inheritdoc />
    public async Task<AccountSessionStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var isDisabled = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => (bool?)user.IsDisabled)
            .SingleOrDefaultAsync(cancellationToken);

        return isDisabled switch
        {
            null => AccountSessionStatus.Missing,
            false => AccountSessionStatus.Active,
            true => AccountSessionStatus.Disabled,
        };
    }
}
