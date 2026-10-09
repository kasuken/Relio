using Microsoft.EntityFrameworkCore;
using Relio.Application.Billing;

namespace Relio.Data.Billing;

/// <summary>
/// Enforces the plan's active-people limit (<see cref="PlanCatalog"/>) inside the data services that
/// add or restore people: create, restore, import, merge and data restore. The UI never duplicates it.
/// </summary>
/// <remarks>
/// <para>
/// With no billing provider (every self-hosted instance) nothing is limited and no query runs. The
/// people services take it as an optional constructor parameter defaulting to <see cref="Unlimited"/>,
/// so tests that build a service directly keep working unchanged.
/// </para>
/// <para>
/// It is a soft limit: two saves racing from two tabs at the limit can both pass the count. That only
/// ever lets a free account go one or two over, which is not worth a lock.
/// </para>
/// <para>
/// A singleton with no <see cref="RelioDbContext"/> of its own: each check runs on the calling data
/// service's context, inside that service's database-lane turn.
/// </para>
/// </remarks>
public sealed class PlanLimits(IBillingProvider billingProvider)
{
    /// <summary>No limits, as on an instance without billing.</summary>
    public static PlanLimits Unlimited { get; } = new(new NullBillingProvider());

    /// <summary>True when plans and their limits apply.</summary>
    public bool Enabled => billingProvider.IsEnabled;

    /// <summary>The active-people limit of the owner's plan right now; null means unlimited.</summary>
    public async Task<int?> GetActivePeopleLimitAsync(
        RelioDbContext dbContext,
        string ownerId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (!Enabled)
        {
            return null;
        }

        var stored = await dbContext.UserSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == ownerId)
            .Select(subscription => new { subscription.Tier, subscription.GracePeriodEndsAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        var tier = stored is null
            ? PlanTier.Free
            : SubscriptionStateRules.EffectiveTier(stored.Tier, stored.GracePeriodEndsAtUtc, nowUtc);
        return PlanCatalog.Get(tier).MaxActivePeople;
    }

    /// <summary>
    /// Throws <see cref="PlanLimitReachedException"/> when making <paramref name="additionalActivePeople"/>
    /// more people active would take the owner over their plan's limit. Archived people do not count.
    /// </summary>
    public async Task EnsureRoomForActivePeopleAsync(
        RelioDbContext dbContext,
        string ownerId,
        int additionalActivePeople,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (additionalActivePeople <= 0)
        {
            return;
        }

        var limit = await GetActivePeopleLimitAsync(dbContext, ownerId, nowUtc, cancellationToken);
        if (limit is not int max)
        {
            return;
        }

        var active = await dbContext.People
            .AsNoTracking()
            .CountAsync(person => person.OwnerId == ownerId && !person.IsArchived, cancellationToken);
        if (active + additionalActivePeople > max)
        {
            throw new PlanLimitReachedException(max);
        }
    }
}
