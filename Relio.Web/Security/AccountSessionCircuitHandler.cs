using Microsoft.AspNetCore.Components.Server.Circuits;
using Relio.Application.Accounts;
using Relio.Application.Security;

namespace Relio.Web.Security;

/// <summary>
/// Checks a fresh account status before processing every inbound activity on an authenticated
/// circuit. A local notification revokes active circuits immediately; the database check catches
/// erasure by another application instance on the next activity.
/// </summary>
public sealed class AccountSessionCircuitHandler(
    IAccountSessionStatusService accountStatus,
    IAccountSessionRevocationNotifier revocations,
    ICurrentUser currentUser) : CircuitHandler, IAsyncDisposable
{
    private readonly IAccountSessionStatusService _accountStatus =
        accountStatus ?? throw new ArgumentNullException(nameof(accountStatus));
    private readonly IAccountSessionRevocationNotifier _revocations =
        revocations ?? throw new ArgumentNullException(nameof(revocations));
    private readonly ICurrentUser _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private IDisposable? _subscription;
    private int _revoked;

    /// <inheritdoc />
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!string.IsNullOrEmpty(userId))
        {
            _subscription = _revocations.Subscribe(userId, RevokeCircuitAsync);
        }

        await base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    /// <inheritdoc />
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return async activity =>
        {
            var userId = _currentUser.UserId;
            if (string.IsNullOrEmpty(userId) || Volatile.Read(ref _revoked) != 0)
            {
                if (Volatile.Read(ref _revoked) == 0)
                {
                    await next(activity);
                }

                return;
            }

            var status = await _accountStatus.GetCurrentStatusAsync();
            if (status != AccountSessionStatus.Active)
            {
                var reason = status == AccountSessionStatus.Missing
                    ? AccountSessionRevocationReason.Deleted
                    : AccountSessionRevocationReason.Disabled;
                await _revocations.RevokeActiveAsync(userId, reason);
                Interlocked.Exchange(ref _revoked, 1);
                return;
            }

            await next(activity);
        };
    }

    /// <inheritdoc />
    public override async Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        await DisposeAsync();
        await base.OnCircuitClosedAsync(circuit, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _subscription, null)?.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task RevokeCircuitAsync(AccountSessionRevocationReason reason)
    {
        Interlocked.Exchange(ref _revoked, 1);
        return Task.CompletedTask;
    }
}
