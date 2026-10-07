using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Web.Metrics;

namespace Relio.Web.Tests.Metrics;

public sealed class ProductActivityCircuitHandlerTests
{
    [Fact]
    public async Task Successful_inbound_activity_records_once_per_UTC_day_even_in_a_long_lived_circuit()
    {
        var start = new DateTimeOffset(2026, 1, 1, 23, 59, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(start);
        var activity = new FakeProductActivityService();
        var handler = CreateHandler(activity, timeProvider, enabled: true, currentUserId: "user-1");
        var callback = CreateInboundHandler(handler);

        await callback(null!);
        await callback(null!);
        activity.Calls.Should().Be(1);

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await callback(null!);

        activity.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Disabled_or_anonymous_activity_never_invokes_the_collector()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var disabledService = new FakeProductActivityService();
        await CreateInboundHandler(CreateHandler(disabledService, timeProvider, enabled: false, currentUserId: "user-1"))
            (null!);
        disabledService.Calls.Should().Be(0);

        var anonymousService = new FakeProductActivityService();
        await CreateInboundHandler(CreateHandler(anonymousService, timeProvider, enabled: true, currentUserId: null))
            (null!);
        anonymousService.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Failed_inbound_work_is_not_counted_as_activity()
    {
        var activity = new FakeProductActivityService();
        var handler = CreateHandler(
            activity,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)),
            enabled: true,
            currentUserId: "user-1");
        var callback = CreateInboundHandler(handler, _ => Task.FromException(new InvalidOperationException()));

        var act = () => callback(null!);

        await act.Should().ThrowAsync<InvalidOperationException>();
        activity.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Collector_failure_is_retried_by_later_activity_on_the_same_day()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var activity = new FakeProductActivityService(failuresRemaining: 1);
        var callback = CreateInboundHandler(
            CreateHandler(activity, timeProvider, enabled: true, currentUserId: "user-1"));

        await callback(null!);
        await callback(null!);
        activity.Calls.Should().Be(2, "the first collection failure leaves the date retryable");

        await callback(null!);

        activity.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Collector_failure_logs_only_exception_type_and_not_private_exception_text()
    {
        const string privateToken = "private-circuit-token-sentinel";
        var logger = new CapturingLogger<ProductActivityCircuitHandler>();
        var activity = new FakeProductActivityService(failuresRemaining: 1, exceptionMessage: privateToken);
        var callback = CreateInboundHandler(
            CreateHandler(
                activity,
                new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)),
                enabled: true,
                currentUserId: "user-1",
                logger: logger));

        await callback(null!);

        var failure = logger.Entries.Should().ContainSingle().Subject;
        failure.Message.Should().Contain(nameof(InvalidOperationException));
        failure.Message.Should().NotContain(privateToken);
        failure.Exception.Should().BeNull();
    }

    private static ProductActivityCircuitHandler CreateHandler(
        FakeProductActivityService activity,
        TimeProvider timeProvider,
        bool enabled,
        string? currentUserId,
        ILogger<ProductActivityCircuitHandler>? logger = null) =>
        new(
            activity,
            new FakeCurrentUser(currentUserId),
            new TestOptionsMonitor(new ProductMetricsOptions { Enabled = enabled }),
            timeProvider,
            logger ?? NullLogger<ProductActivityCircuitHandler>.Instance);

    private static Func<CircuitInboundActivityContext, Task> CreateInboundHandler(
        ProductActivityCircuitHandler handler,
        Func<CircuitInboundActivityContext, Task>? next = null) =>
        handler.CreateInboundActivityHandler(next ?? (_ => Task.CompletedTask));

    private sealed class FakeCurrentUser(string? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;

        public string? UserId => userId;
    }

    private sealed class FakeProductActivityService : IProductActivityService
    {
        private int _failuresRemaining;
        private readonly string? _exceptionMessage;

        public FakeProductActivityService(int failuresRemaining = 0, string? exceptionMessage = null)
        {
            _failuresRemaining = failuresRemaining;
            _exceptionMessage = exceptionMessage;
        }

        public int Calls { get; private set; }

        public Task RecordAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (_failuresRemaining > 0)
            {
                _failuresRemaining--;
                return Task.FromException(new InvalidOperationException(_exceptionMessage));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Entries { get; } = [];

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
                Entries.Add(new CapturedLog(logLevel, formatter(state, exception), exception));
            }
        }
    }

    private sealed record CapturedLog(LogLevel Level, string Message, Exception? Exception);

    private sealed class TestOptionsMonitor(ProductMetricsOptions value) : IOptionsMonitor<ProductMetricsOptions>
    {
        public ProductMetricsOptions CurrentValue => value;

        public ProductMetricsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ProductMetricsOptions, string?> listener) => null;
    }
}
