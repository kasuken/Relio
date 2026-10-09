using Microsoft.EntityFrameworkCore;
using Relio.Application.Billing;
using Relio.Data.Billing;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Tests.Billing;

/// <summary>Shared setup for the billing tests: an InMemory database, users and subscriptions.</summary>
internal static class BillingTestHarness
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase($"billing-{Guid.NewGuid()}").Options;

    public static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database, TimeProvider clock) =>
        new(database, clock, FieldProtector);

    public static async Task<string> AddUserAsync(RelioDbContext dbContext, string id, string? email = null)
    {
        dbContext.Users.Add(new RelioUser { Id = id, UserName = email ?? $"{id}@example.com", Email = email ?? $"{id}@example.com" });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return id;
    }

    public static async Task AddSubscriptionAsync(RelioDbContext dbContext, UserSubscription subscription)
    {
        dbContext.UserSubscriptions.Add(subscription);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    public static async Task AddPeopleAsync(RelioDbContext dbContext, string ownerId, int active, int archived = 0)
    {
        for (var index = 0; index < active; index++)
        {
            dbContext.People.Add(new Person { OwnerId = ownerId, FirstName = $"Active {index}" });
        }

        for (var index = 0; index < archived; index++)
        {
            dbContext.People.Add(new Person { OwnerId = ownerId, FirstName = $"Archived {index}", IsArchived = true });
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    public static Task<UserSubscription?> ReadSubscriptionAsync(RelioDbContext dbContext, string userId) =>
        dbContext.UserSubscriptions.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId);

    public static UserSubscription Pro(string userId, string customerId = "cus_test", DateTime? graceEndsAtUtc = null) => new()
    {
        UserId = userId,
        Tier = PlanTier.Pro,
        BillingProviderCustomerId = customerId,
        BillingProviderSubscriptionId = $"sub_{customerId}",
        PlanRenewsAtUtc = Now.UtcDateTime.AddMonths(1),
        GracePeriodEndsAtUtc = graceEndsAtUtc,
    };

    public static ParsedBillingEvent ProEvent(
        string eventId,
        string? userId,
        DateTime? occurredAtUtc = null,
        string customerId = "cus_test",
        string subscriptionId = "sub_test") =>
        new(
            ProviderEventId: eventId,
            EventType: "customer.subscription.updated",
            TargetUserId: userId,
            NewTier: PlanTier.Pro,
            PeriodEndsAtUtc: Now.UtcDateTime.AddYears(1),
            BillingProviderCustomerId: customerId,
            BillingProviderSubscriptionId: subscriptionId,
            ClearsGracePeriod: true,
            OccurredAtUtc: occurredAtUtc ?? Now.UtcDateTime,
            HasCancellationSchedule: true,
            CancelsAtUtc: null,
            MayResolveByCustomer: true);
}
