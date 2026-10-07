using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Options;
using Relio.Application.Metrics;
using Relio.Application.Security;

namespace Relio.Web.Metrics;

/// <summary>
/// Observes successful interactive circuit activity for the current authenticated user, at most
/// once per UTC date. It stores no circuit id, event payload, route, device, or IP information.
/// </summary>
public sealed class ProductActivityCircuitHandler(
    IProductActivityService activityService,
    ICurrentUser currentUser,
    IOptionsMonitor<ProductMetricsOptions> options,
    TimeProvider timeProvider,
    ILogger<ProductActivityCircuitHandler> logger) : CircuitHandler
{
    private readonly IProductActivityService _activityService =
        activityService ?? throw new ArgumentNullException(nameof(activityService));
    private readonly ICurrentUser _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly IOptionsMonitor<ProductMetricsOptions> _options =
        options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<ProductActivityCircuitHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _activityGate = new(1, 1);
    private DateOnly? _lastRecordedOnUtc;

    /// <inheritdoc />
    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        await base.OnConnectionUpAsync(circuit, cancellationToken);
        await RecordIfNeededAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return async activity =>
        {
            await next(activity);
            await RecordIfNeededAsync(CancellationToken.None);
        };
    }

    private async Task RecordIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            _lastRecordedOnUtc = null;
            return;
        }

        if (!_currentUser.IsAuthenticated || string.IsNullOrEmpty(_currentUser.UserId))
        {
            _lastRecordedOnUtc = null;
            return;
        }

        await _activityGate.WaitAsync(cancellationToken);
        try
        {
            if (!_options.CurrentValue.Enabled)
            {
                _lastRecordedOnUtc = null;
                return;
            }

            var todayUtc = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
            if (_lastRecordedOnUtc == todayUtc)
            {
                return;
            }

            try
            {
                await _activityService.RecordAsync(cancellationToken);
                _lastRecordedOnUtc = todayUtc;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // This optional collector must not block the page's interactive work. Keep failure
                // diagnostics useful without attaching a database exception that could contain keys.
                // Do not mark the date on failure; the next inbound activity retries the collection.
                _logger.LogError(
                    "Product activity recording failed and will retry on later activity; exception type {ExceptionType}.",
                    exception.GetType().Name);
            }
        }
        finally
        {
            _activityGate.Release();
        }
    }
}
