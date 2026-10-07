using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Data.Metrics;
using Relio.Data.Tests.People;
using Relio.Domain;

namespace Relio.Data.Tests.Metrics;

public sealed class ProductActivityServiceTests
{
    private const string OwnerId = "metrics-owner";
    private static readonly DateTimeOffset InitialInstant = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAsync_when_disabled_requires_a_user_before_returning_without_querying()
    {
        var queries = new MetricsTestSupport.QueryCountingInterceptor();
        var saves = new MetricsTestSupport.SaveCountingInterceptor();
        await using var dbContext = MetricsTestSupport.CreateDbContext(
            Guid.NewGuid().ToString(),
            new FakeTimeProvider(InitialInstant),
            queries,
            saves);
        var service = CreateService(dbContext, null, enabled: false, new FakeTimeProvider(InitialInstant));

        var act = () => service.RecordAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
        queries.QueryCount.Should().Be(0);
        saves.SaveCount.Should().Be(0);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_when_disabled_and_authenticated_does_not_query_or_save()
    {
        var queries = new MetricsTestSupport.QueryCountingInterceptor();
        var saves = new MetricsTestSupport.SaveCountingInterceptor();
        await using var dbContext = MetricsTestSupport.CreateDbContext(
            Guid.NewGuid().ToString(),
            new FakeTimeProvider(InitialInstant),
            queries,
            saves);
        var service = CreateService(dbContext, OwnerId, enabled: false, new FakeTimeProvider(InitialInstant));

        await service.RecordAsync();

        queries.QueryCount.Should().Be(0);
        saves.SaveCount.Should().Be(0);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_when_enabled_requires_an_authenticated_current_user_without_querying()
    {
        var queries = new MetricsTestSupport.QueryCountingInterceptor();
        await using var dbContext = MetricsTestSupport.CreateDbContext(
            Guid.NewGuid().ToString(),
            new FakeTimeProvider(InitialInstant),
            queries);
        var service = CreateService(dbContext, null, enabled: true, new FakeTimeProvider(InitialInstant));

        var act = () => service.RecordAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
        queries.QueryCount.Should().Be(0);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_creates_only_the_current_users_metadata_row()
    {
        var timeProvider = new FakeTimeProvider(InitialInstant);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        await MetricsTestSupport.CreateUserAsync(dbContext, OwnerId);
        const string otherOwnerId = "other-metrics-owner";
        await MetricsTestSupport.CreateUserAsync(dbContext, otherOwnerId);

        await CreateService(dbContext, OwnerId, enabled: true, timeProvider).RecordAsync();
        await CreateService(dbContext, otherOwnerId, enabled: true, timeProvider).RecordAsync();

        var activities = await dbContext.Set<ProductActivity>()
            .AsNoTracking()
            .ToListAsync();
        activities.Should().HaveCount(2);
        activities.Select(activity => activity.OwnerId)
            .Should().BeEquivalentTo([OwnerId, otherOwnerId]);
        var activity = activities.Single(candidate => candidate.OwnerId == OwnerId);
        activity.OwnerId.Should().Be(OwnerId);
        activity.CohortStartedOnUtc.Should().Be(new DateOnly(2026, 1, 1));
        activity.LastActiveOnUtc.Should().Be(activity.CohortStartedOnUtc);
        activity.ReturnedInDays30To59.Should().BeFalse();
        activity.RetentionExpiresAtUtc.Should().Be(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task RecordAsync_does_not_create_a_contribution_for_a_missing_or_disabled_account()
    {
        var timeProvider = new FakeTimeProvider(InitialInstant);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        await MetricsTestSupport.CreateUserAsync(dbContext, "disabled-metrics-owner", disabled: true);
        var missingService = CreateService(dbContext, "missing-metrics-owner", enabled: true, timeProvider);
        var disabledService = CreateService(dbContext, "disabled-metrics-owner", enabled: true, timeProvider);

        await missingService.RecordAsync();
        await disabledService.RecordAsync();

        (await dbContext.Set<ProductActivity>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecordAsync_and_disabled_cleanup_serialize_across_contexts_and_leave_no_row()
    {
        var databaseName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(InitialInstant);
        await using (var seedContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider))
        {
            await MetricsTestSupport.CreateUserAsync(seedContext, OwnerId);
        }

        var innerGate = new ProductMetricsCollectionGate();
        var observedGate = new ObservedCollectionGate(innerGate);
        var options = new MutableProductMetricsOptionsMonitor(
            new ProductMetricsOptions { Enabled = true });
        var pausedSave = new PausingSaveInterceptor();
        await using var writerContext = MetricsTestSupport.CreateDbContext(
            databaseName,
            timeProvider,
            pausedSave);
        await using var cleanupContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider);
        var runner = new ProductMetricsRetentionRunner(
            cleanupContext,
            options,
            observedGate,
            timeProvider);
        var writer = new ProductActivityService(
            writerContext,
            new FakeCurrentUser(OwnerId),
            options,
            observedGate,
            timeProvider);
        var writeTask = writer.RecordAsync();

        Task<long>? cleanupTask = null;
        try
        {
            await pausedSave.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            options.Set(new ProductMetricsOptions { Enabled = false });

            cleanupTask = runner.RunAsync();

            await observedGate.SecondEntryAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cleanupTask.IsCompleted.Should().BeFalse("cleanup must wait while the writer holds the shared gate");
        }
        finally
        {
            pausedSave.Release();
            if (cleanupTask is not null)
            {
                await Task.WhenAll(writeTask, cleanupTask);
            }
            else
            {
                await writeTask;
            }
        }

        await using var verifyContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider);
        (await verifyContext.Set<ProductActivity>()
            .AsNoTracking()
            .AnyAsync(activity => activity.OwnerId == OwnerId))
            .Should().BeFalse("the disabled purge runs after the in-flight writer releases the gate");
        writerContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_sets_return_only_inside_the_day_30_to_59_window_and_keeps_expiry_fixed()
    {
        var timeProvider = new FakeTimeProvider(InitialInstant);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        await MetricsTestSupport.CreateUserAsync(dbContext, OwnerId);
        var service = CreateService(dbContext, OwnerId, enabled: true, timeProvider);
        await service.RecordAsync();
        var originalExpiry = ProductActivityCohortRules.RetentionExpiresAtUtc(new DateOnly(2026, 1, 1));

        timeProvider.Advance(TimeSpan.FromDays(29));
        await service.RecordAsync();
        (await dbContext.Set<ProductActivity>().AsNoTracking().SingleAsync()).ReturnedInDays30To59.Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromDays(1));
        await service.RecordAsync();
        var returned = await dbContext.Set<ProductActivity>().AsNoTracking().SingleAsync();
        returned.ReturnedInDays30To59.Should().BeTrue();
        returned.LastActiveOnUtc.Should().Be(new DateOnly(2026, 1, 31));
        returned.RetentionExpiresAtUtc.Should().Be(originalExpiry, "the expiry is fixed from cohort start");

        timeProvider.Advance(TimeSpan.FromDays(30));
        await service.RecordAsync();
        (await dbContext.Set<ProductActivity>().AsNoTracking().SingleAsync())
            .ReturnedInDays30To59.Should().BeTrue("the day-60 activity does not undo a qualifying return");
    }

    [Fact]
    public async Task RecordAsync_does_not_count_a_first_return_on_day_60()
    {
        var timeProvider = new FakeTimeProvider(InitialInstant);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        await MetricsTestSupport.CreateUserAsync(dbContext, OwnerId);
        var service = CreateService(dbContext, OwnerId, enabled: true, timeProvider);
        await service.RecordAsync();

        timeProvider.Advance(TimeSpan.FromDays(60));
        await service.RecordAsync();

        var activity = await dbContext.Set<ProductActivity>().AsNoTracking().SingleAsync();
        activity.ReturnedInDays30To59.Should().BeFalse();
        activity.LastActiveOnUtc.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public async Task RecordAsync_replaces_an_expired_row_without_carrying_its_id_or_creation_time()
    {
        var cohortStart = new DateOnly(2026, 1, 1);
        var today = cohortStart.AddDays(ProductActivityCohortRules.RetentionDays);
        var now = new DateTimeOffset(today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        await MetricsTestSupport.CreateUserAsync(dbContext, OwnerId);
        var expired = new ProductActivity
        {
            OwnerId = OwnerId,
            CohortStartedOnUtc = cohortStart,
            LastActiveOnUtc = cohortStart.AddDays(30),
            ReturnedInDays30To59 = true,
            RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(cohortStart),
        };
        dbContext.Set<ProductActivity>().Add(expired);
        await dbContext.SaveChangesAsync();
        var oldId = expired.Id;
        var oldCreatedAtUtc = now.AddDays(-180).UtcDateTime;
        expired.CreatedAtUtc = oldCreatedAtUtc;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await CreateService(dbContext, OwnerId, enabled: true, timeProvider).RecordAsync();

        var replacement = await dbContext.Set<ProductActivity>().AsNoTracking().SingleAsync();
        replacement.Id.Should().NotBe(oldId);
        replacement.CohortStartedOnUtc.Should().Be(today);
        replacement.LastActiveOnUtc.Should().Be(today);
        replacement.ReturnedInDays30To59.Should().BeFalse();
        replacement.RetentionExpiresAtUtc.Should().Be(ProductActivityCohortRules.RetentionExpiresAtUtc(today));
        replacement.CreatedAtUtc.Should().Be(now.UtcDateTime);
        replacement.CreatedAtUtc.Should().NotBe(oldCreatedAtUtc);
    }

    [Fact]
    public async Task RecordAsync_sanitizes_and_does_not_suppress_unrelated_database_write_failures()
    {
        var databaseName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(InitialInstant);
        await using (var seedContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider))
        {
            await MetricsTestSupport.CreateUserAsync(seedContext, OwnerId);
        }

        await using var failingContext = MetricsTestSupport.CreateDbContext(
            databaseName,
            timeProvider,
            new FailingSaveInterceptor());
        var service = CreateService(failingContext, OwnerId, enabled: true, timeProvider);

        var exception = await FluentActions.Awaiting(() => service.RecordAsync())
            .Should().ThrowAsync<ProductActivityWriteException>();

        exception.Which.Message.Should().Be("Product activity could not be recorded.");
        exception.Which.InnerException.Should().BeNull();
        exception.Which.Message.Should().NotContain(OwnerId);
        failingContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static ProductActivityService CreateService(
        RelioDbContext dbContext,
        string? currentUserId,
        bool enabled,
        TimeProvider timeProvider,
        IProductMetricsCollectionGate? collectionGate = null) =>
        new(
            dbContext,
            new FakeCurrentUser(currentUserId),
            MetricsTestSupport.Options(enabled),
            collectionGate ?? new ProductMetricsCollectionGate(),
            timeProvider);

    private sealed class FailingSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>(
                new DbUpdateException("private owner key and database value"));
    }

    private sealed class PausingSaveInterceptor : SaveChangesInterceptor
    {
        public TaskCompletionSource<bool> SaveEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<bool> _release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult(true);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveEntered.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ObservedCollectionGate(IProductMetricsCollectionGate inner)
        : IProductMetricsCollectionGate
    {
        private int _entryCount;

        public TaskCompletionSource<bool> SecondEntryAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _entryCount) == 2)
            {
                SecondEntryAttempted.TrySetResult(true);
            }

            return await inner.EnterAsync(cancellationToken);
        }
    }

    private sealed class MutableProductMetricsOptionsMonitor(ProductMetricsOptions initial)
        : Microsoft.Extensions.Options.IOptionsMonitor<ProductMetricsOptions>
    {
        public ProductMetricsOptions CurrentValue { get; private set; } = initial;

        public ProductMetricsOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ProductMetricsOptions, string?> listener) => null;

        public void Set(ProductMetricsOptions options) => CurrentValue = options;
    }
}
