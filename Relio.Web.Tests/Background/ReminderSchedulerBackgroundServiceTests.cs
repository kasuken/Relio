using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Reminders;
using Relio.Web.Background;

namespace Relio.Web.Tests.Background;

public class ReminderSchedulerBackgroundServiceTests
{
    [Fact]
    public async Task Background_service_executes_runner_on_schedule()
    {
        var runner = new FakeRunner();
        var services = new ServiceCollection();
        services.AddScoped<IReminderSchedulerRunner>(_ => runner);
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var timeProvider = new FakeTimeProvider();
        var options = Options.Create(new ReminderSchedulerOptions
        {
            Enabled = true,
            CheckInterval = TimeSpan.FromMinutes(30),
        });

        var service = new ReminderSchedulerBackgroundService(
            scopeFactory,
            timeProvider,
            options,
            NullLogger<ReminderSchedulerBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        var runTask = service.StartAsync(cts.Token);

        // Allow the first iteration to complete:
        await runner.WaitUntilRunCountAsync(1);
        runner.RunCount.Should().Be(1);

        // Advance time by 30 minutes to trigger the next execution:
        timeProvider.Advance(TimeSpan.FromMinutes(30));
        await runner.WaitUntilRunCountAsync(2);
        runner.RunCount.Should().Be(2);

        // Cancel and stop:
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
        await runTask;
    }

    [Fact]
    public async Task Background_service_exits_immediately_when_disabled()
    {
        var runner = new FakeRunner();
        var services = new ServiceCollection();
        services.AddScoped<IReminderSchedulerRunner>(_ => runner);
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var timeProvider = new FakeTimeProvider();
        var options = Options.Create(new ReminderSchedulerOptions
        {
            Enabled = false,
            CheckInterval = TimeSpan.FromHours(1),
        });

        var service = new ReminderSchedulerBackgroundService(
            scopeFactory,
            timeProvider,
            options,
            NullLogger<ReminderSchedulerBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        runner.RunCount.Should().Be(0);
    }

    [Fact]
    public async Task Background_service_responds_to_cancellation_during_delay()
    {
        var runner = new FakeRunner();
        var services = new ServiceCollection();
        services.AddScoped<IReminderSchedulerRunner>(_ => runner);
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var timeProvider = new FakeTimeProvider();
        var options = Options.Create(new ReminderSchedulerOptions
        {
            Enabled = true,
            CheckInterval = TimeSpan.FromHours(1),
        });

        var service = new ReminderSchedulerBackgroundService(
            scopeFactory,
            timeProvider,
            options,
            NullLogger<ReminderSchedulerBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = service.StartAsync(cts.Token);

        await runner.WaitUntilRunCountAsync(1);
        runner.RunCount.Should().Be(1);

        // Cancel while sleeping:
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
        await startTask;
    }

    [Fact]
    public async Task Background_service_handles_runner_exception_without_crashing()
    {
        var runner = new FakeRunner { ShouldThrowOnFirstRun = true };
        var services = new ServiceCollection();
        services.AddScoped<IReminderSchedulerRunner>(_ => runner);
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var timeProvider = new FakeTimeProvider();
        var options = Options.Create(new ReminderSchedulerOptions
        {
            Enabled = true,
            CheckInterval = TimeSpan.FromHours(1),
        });

        var service = new ReminderSchedulerBackgroundService(
            scopeFactory,
            timeProvider,
            options,
            NullLogger<ReminderSchedulerBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = service.StartAsync(cts.Token);

        // First run threw, but service caught it and waited for interval:
        await runner.WaitUntilRunCountAsync(1);
        runner.RunCount.Should().Be(1);

        // Advance time to allow second run (which will succeed):
        timeProvider.Advance(TimeSpan.FromHours(1));
        await runner.WaitUntilRunCountAsync(2);
        runner.RunCount.Should().Be(2);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
        await startTask;
    }

    [Fact]
    public async Task Background_service_does_not_log_private_exception_details()
    {
        const string privateValue = "private-reminder-content-sentinel";
        var runner = new FakeRunner
        {
            ShouldThrowOnFirstRun = true,
            ExceptionMessage = privateValue,
        };
        var services = new ServiceCollection();
        services.AddScoped<IReminderSchedulerRunner>(_ => runner);
        using var provider = services.BuildServiceProvider();
        var logger = new RecordingLogger<ReminderSchedulerBackgroundService>();
        var service = new ReminderSchedulerBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(),
            Options.Create(new ReminderSchedulerOptions
            {
                Enabled = true,
                CheckInterval = TimeSpan.FromHours(1),
            }),
            logger);

        using var cts = new CancellationTokenSource();
        var startTask = service.StartAsync(cts.Token);

        await runner.WaitUntilRunCountAsync(1);
        await logger.WaitUntilCountAsync(2);
        var logs = string.Join(Environment.NewLine, logger.Entries.Select(entry => entry.Message));
        logs.Should().NotContain(privateValue);

        var failure = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        failure.Message.Should().Contain(nameof(InvalidOperationException));
        failure.Exception.Should().BeNull();

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
        await startTask;
    }

    private sealed class FakeRunner : IReminderSchedulerRunner
    {
        private readonly TaskCompletionSource _tcs = new();
        private readonly SemaphoreSlim _signal = new(0);

        public int RunCount { get; private set; }
        public bool ShouldThrowOnFirstRun { get; set; }
        public string? ExceptionMessage { get; set; }

        public Task<int> RunDueRemindersJobAsync(CancellationToken cancellationToken = default)
        {
            RunCount++;
            _signal.Release();

            if (ShouldThrowOnFirstRun && RunCount == 1)
            {
                throw new InvalidOperationException(ExceptionMessage ?? "Simulated transient runner error");
            }

            return Task.FromResult(RunCount);
        }

        public async Task WaitUntilRunCountAsync(int targetCount, int timeoutMs = 5000)
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            while (RunCount < targetCount)
            {
                await _signal.WaitAsync(timeoutCts.Token);
            }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly SemaphoreSlim _signal = new(0);

        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
            }

            _signal.Release();
        }

        public async Task WaitUntilCountAsync(int targetCount, int timeoutMs = 5000)
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            while (true)
            {
                lock (Entries)
                {
                    if (Entries.Count >= targetCount)
                    {
                        return;
                    }
                }

                await _signal.WaitAsync(timeoutCts.Token);
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
