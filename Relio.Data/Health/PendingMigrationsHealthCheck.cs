using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Relio.Data.Health;

/// <summary>
/// Readiness check that is healthy only when the database schema is current: every EF Core
/// migration shipped with this build has been applied.
/// </summary>
/// <remarks>
/// <c>AddDbContextCheck</c> only proves the database can be reached. Relio applies migrations on
/// startup (<c>Database:ApplyMigrationsOnStartup</c>), so a ready instance should also mean the
/// schema matches the code; this makes <c>/health/ready</c> say so, and lets a deployment
/// pipeline treat a 200 as "connected and migrated". It reads migration ids only, never data.
/// Exceptions are deliberately not caught: the health check framework reports them as unhealthy
/// without exposing the message on the endpoint.
/// </remarks>
public sealed class PendingMigrationsHealthCheck(RelioDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // The InMemory provider used by tests and local runs has no migrations to be behind on.
        if (!dbContext.Database.IsRelational())
        {
            return HealthCheckResult.Healthy("Non-relational provider; there are no migrations.");
        }

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        return pending.Count == 0
            ? HealthCheckResult.Healthy("All migrations are applied.")
            : HealthCheckResult.Unhealthy($"{pending.Count} pending migration(s).");
    }
}
