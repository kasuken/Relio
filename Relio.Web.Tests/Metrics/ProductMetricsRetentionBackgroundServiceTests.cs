using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Metrics;
using Relio.Web.Diagnostics;
using Relio.Web.Metrics;
using Relio.Web.Tests.Infrastructure;

namespace Relio.Web.Tests.Metrics;

public sealed class ProductMetricsRetentionBackgroundServiceTests
{
    [Fact]
    public async Task Startup_runs_cleanup_and_disabling_wakes_the_runner_without_waiting_a_day()
    {
        var runner = new CountingRetentionRunner();
        var options = new MutableOptionsMonitor(new ProductMetricsOptions { Enabled = true });
        var services = new ServiceCollection();
        services.AddSingleton<IProductMetricsRetentionRunner>(runner);
        using var provider = services.BuildServiceProvider();
        var backgroundService = new ProductMetricsRetentionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ProductMetricsRetentionBackgroundService>.Instance);

        await backgroundService.StartAsync(CancellationToken.None);
        try
        {
            runner.CallCount.Should().Be(1, "startup cleanup happens before the hosted loop waits");

            options.Set(new ProductMetricsOptions { Enabled = false });
            await runner.WaitForSecondCallAsync();

            runner.CallCount.Should().Be(2, "disabling wakes cleanup immediately instead of waiting for the daily timer");
        }
        finally
        {
            await backgroundService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Retention_failure_emits_stable_event_without_note_or_difficult_moment_values()
    {
        const string noteSentinel = "private-note-text-sentinel-123";
        const string difficultMomentSentinel = "private-difficult-moment-sentinel-456";
        var services = new ServiceCollection();
        services.AddSingleton<IProductMetricsRetentionRunner>(
            new ThrowingRetentionRunner($"{noteSentinel} {difficultMomentSentinel}"));
        using var provider = services.BuildServiceProvider();
        var capture = new CapturingLoggerProvider();
        var backgroundService = new ProductMetricsRetentionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MutableOptionsMonitor(new ProductMetricsOptions { Enabled = true }),
            new FakeTimeProvider(),
            new CapturingLogger<ProductMetricsRetentionBackgroundService>(capture));

        var startup = () => backgroundService.StartAsync(CancellationToken.None);

        var thrown = (await startup.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Message.Should().NotContain(noteSentinel).And.NotContain(difficultMomentSentinel);
        var failure = capture.Entries.Should().ContainSingle().Subject;
        failure.EventId.Id.Should().Be(RelioOperationalEvents.ProductMetricsRetentionFailureId);
        failure.EventId.Name.Should().Be("RelioProductMetricsRetentionFailure");
        failure.Message.Should().Contain(nameof(InvalidOperationException));
        failure.Exception.Should().BeNull();
        capture.AllText.Should().NotContain(noteSentinel).And.NotContain(difficultMomentSentinel);
    }

    private sealed class ThrowingRetentionRunner(string message) : IProductMetricsRetentionRunner
    {
        public Task<long> RunAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class CapturingLogger<T>(CapturingLoggerProvider provider) : ILogger<T>
    {
        private readonly ILogger _inner = provider.CreateLogger(typeof(T).FullName!);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _inner.Log(logLevel, eventId, state, exception, formatter);
    }

    private sealed class CountingRetentionRunner : IProductMetricsRetentionRunner
    {
        private readonly TaskCompletionSource<bool> _secondCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<long> RunAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _callCount) >= 2)
            {
                _secondCall.TrySetResult(true);
            }

            return Task.FromResult(0L);
        }

        public Task WaitForSecondCallAsync() => _secondCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class MutableOptionsMonitor(ProductMetricsOptions initial) : IOptionsMonitor<ProductMetricsOptions>
    {
        private Action<ProductMetricsOptions, string?>? _listener;

        public ProductMetricsOptions CurrentValue { get; private set; } = initial;

        public ProductMetricsOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<ProductMetricsOptions, string?> listener)
        {
            _listener += listener;
            return new Subscription(() => _listener -= listener);
        }

        public void Set(ProductMetricsOptions options)
        {
            CurrentValue = options;
            _listener?.Invoke(options, null);
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
