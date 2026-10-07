using Microsoft.EntityFrameworkCore;
using Relio.Application.Reminders;
using Relio.Domain;

namespace Relio.Data.Reminders;

/// <summary>
/// EF Core implementation of <see cref="IUnsubscribeService"/> (Issue #40).
/// Does not require authentication: matches the user by their secret unsubscribe token.
/// </summary>
public sealed class UnsubscribeService(RelioDbContext dbContext) : IUnsubscribeService
{
    /// <inheritdoc />
    public async Task<bool> UnsubscribeAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var profile = await dbContext.UserProfiles
                .FirstOrDefaultAsync(p => p.UnsubscribeToken == token, cancellationToken);

            if (profile is null)
            {
                return false;
            }

            profile.ReminderEmailDelivery = ReminderEmailDelivery.None;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
