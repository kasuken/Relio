using Microsoft.Extensions.Options;
using Relio.Application.Metrics;

namespace Relio.Web.Metrics;

/// <summary>
/// Runs product activity expiry at startup and daily, and wakes immediately when configuration
/// changes to disable collection.
/// </summary>
public sealed class ProductMetricsRetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ProductMetricsOptions> options,
    TimeProvider timeProvider,
    ILogger<ProductMetricsRetentionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan RetentionCheckInterval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly IOptionsMonitor<ProductMetricsOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<ProductMetricsRetentionBackgroundService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _disabledSignal = new(0, 1);
    private IDisposable? _optionsSubscription;

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _optionsSubscription = _options.OnChange((currentOptions, _) =>
        {
            if (!currentOptions.Enabled)
            {
                try
                {
                    _disabledSignal.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Multiple disable notifications can share one pending cleanup wake-up.
                }
            }
        });

        // Complete the initial purge before the host accepts requests. If disabled, the runner
        // removes every contribution; if enabled, it removes expired rows.
        await RunCleanupAsync(cancellationToken, failStartupOnError: true);
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await WaitForNextRunAsync(stoppingToken);
                await RunCleanupAsync(stoppingToken, failStartupOnError: false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _optionsSubscription?.Dispose();
        await base.StopAsync(cancellationToken);
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken, bool failStartupOnError)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IProductMetricsRetentionRunner>();
            var removedCount = await runner.RunAsync(cancellationToken);
            if (removedCount > 0)
            {
                _logger.LogInformation(
                    "Product metrics retention removed {DeletedCount} contribution rows.",
                    removedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Product metrics retention failed with exception type {ExceptionType}.",
                exception.GetType().Name);

            if (failStartupOnError)
            {
                throw new InvalidOperationException("Product metrics retention cleanup could not complete.");
            }
        }
    }

    private async Task WaitForNextRunAsync(CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var dailyTimer = Task.Delay(RetentionCheckInterval, _timeProvider, waitCancellation.Token);
        var disabledSignal = _disabledSignal.WaitAsync(waitCancellation.Token);

        await Task.WhenAny(dailyTimer, disabledSignal);
        waitCancellation.Cancel();

        try
        {
            await Task.WhenAll(dailyTimer, disabledSignal);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The losing wait was cancelled after either the daily timer or disable signal fired.
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
