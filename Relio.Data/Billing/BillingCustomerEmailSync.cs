using Microsoft.EntityFrameworkCore;
using Relio.Application.Billing;

namespace Relio.Data.Billing;

/// <summary>EF-backed <see cref="IBillingCustomerEmailSync"/>.</summary>
public sealed class BillingCustomerEmailSync(
    RelioDbContext dbContext,
    IBillingProvider billingProvider) : IBillingCustomerEmailSync
{
    /// <inheritdoc />
    public async Task<bool> SyncAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        if (!billingProvider.IsEnabled)
        {
            return true;
        }

        var customerId = await dbContext.UserSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => subscription.BillingProviderCustomerId)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrEmpty(customerId)
            || await billingProvider.UpdateCustomerEmailAsync(customerId, email, cancellationToken);
    }
}
