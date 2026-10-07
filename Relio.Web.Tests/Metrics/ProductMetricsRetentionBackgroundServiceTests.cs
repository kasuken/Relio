using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Metrics;
using Relio.Web.Metrics;

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
