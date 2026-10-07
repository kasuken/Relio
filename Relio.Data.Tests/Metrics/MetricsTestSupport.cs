using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Application.Metrics;
using Relio.Data.Identity;

namespace Relio.Data.Tests.Metrics;

internal static class MetricsTestSupport
{
    public static RelioDbContext CreateDbContext(
        string databaseName,
        TimeProvider? timeProvider = null,
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptors);
        var dbContext = new RelioDbContext(builder.Options, timeProvider ?? TimeProvider.System, FieldProtector);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public static IOptionsMonitor<ProductMetricsOptions> Options(bool enabled) =>
        new StaticOptionsMonitor<ProductMetricsOptions>(new ProductMetricsOptions { Enabled = enabled });

    public static async Task<RelioUser> CreateUserAsync(
        RelioDbContext dbContext,
        string id,
        bool disabled = false,
        bool administrator = false)
    {
        var email = $"{id}@example.com";
        var user = new RelioUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            IsDisabled = disabled,
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        if (administrator)
        {
            var role = await dbContext.Roles.AsNoTracking()
                .SingleAsync(existing => existing.Name == RelioRoles.Administrator);
            dbContext.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = role.Id });
            await dbContext.SaveChangesAsync();
        }

        dbContext.ChangeTracker.Clear();
        return user;
    }

    public sealed class QueryCountingInterceptor : IQueryExpressionInterceptor
    {
        public int QueryCount { get; private set; }

        public void Reset() => QueryCount = 0;

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            QueryCount++;
            return queryExpression;
        }
    }

    public sealed class SaveCountingInterceptor : SaveChangesInterceptor
    {
        public int SaveCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public TOptions CurrentValue => value;

        public TOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
