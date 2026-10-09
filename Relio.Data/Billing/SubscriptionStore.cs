using Microsoft.EntityFrameworkCore;

namespace Relio.Data.Billing;

/// <summary>Loads (or adds) a user's <see cref="UserSubscription"/> row, tracked, for an update.</summary>
internal static class SubscriptionStore
{
    public static async Task<UserSubscription> GetOrAddTrackedAsync(
        RelioDbContext dbContext,
        string userId,
        CancellationToken cancellationToken)
    {
        var subscription = await dbContext.UserSubscriptions
            .SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (subscription is not null)
        {
            return subscription;
        }

        subscription = new UserSubscription { UserId = userId };
        dbContext.UserSubscriptions.Add(subscription);
        return subscription;
    }
}
