using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Relio.Data.Health;

namespace Relio.Data.Tests.Health;

public class PendingMigrationsHealthCheckTests
{
    [Fact]
    public async Task A_non_relational_provider_is_healthy_because_it_has_no_migrations()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase($"health-{Guid.NewGuid()}")
            .Options;
        await using var dbContext = new RelioDbContext(options, TimeProvider.System, FieldProtector);
        var check = new PendingMigrationsHealthCheck(dbContext);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
