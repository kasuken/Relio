namespace Relio.Web.Security;

/// <summary>The reason a live circuit's account session is no longer permitted.</summary>
public enum AccountSessionRevocationReason
{
    /// <summary>The account was permanently erased.</summary>
    Deleted,

    /// <summary>An Administrator disabled the account.</summary>
    Disabled,
}

/// <summary>
/// Notifies only currently subscribed circuits when an account is erased or disabled. The
/// implementation stores no history or tombstone; other instances verify account existence on
/// their next inbound circuit activity.
/// </summary>
public interface IAccountSessionRevocationNotifier
{
    /// <summary>Subscribes one active circuit or boundary to a specific account's revocation.</summary>
    /// <param name="userId">The internal account id captured by the active session.</param>
    /// <param name="callback">The callback to await when that session is revoked.</param>
    /// <returns>A subscription that must be disposed when its circuit or component ends.</returns>
    IDisposable Subscribe(string userId, Func<AccountSessionRevocationReason, Task> callback);

    /// <summary>Notifies currently active subscribers and retains no revocation record.</summary>
    /// <param name="userId">The internal account id being revoked.</param>
    /// <param name="reason">Why the session is revoked.</param>
    Task RevokeActiveAsync(string userId, AccountSessionRevocationReason reason);
}

/// <summary>Thread-safe in-process registry of active account-session subscribers.</summary>
public sealed class AccountSessionRevocationNotifier : IAccountSessionRevocationNotifier
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<long, Func<AccountSessionRevocationReason, Task>>> _subscribers =
        new(StringComparer.Ordinal);
    private long _nextSubscriptionId;

    /// <inheritdoc />
    public IDisposable Subscribe(string userId, Func<AccountSessionRevocationReason, Task> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(callback);

        lock (_sync)
        {
            var id = ++_nextSubscriptionId;
            if (!_subscribers.TryGetValue(userId, out var accountSubscribers))
            {
                accountSubscribers = [];
                _subscribers.Add(userId, accountSubscribers);
            }

            accountSubscribers.Add(id, callback);
            return new Subscription(this, userId, id);
        }
    }

    /// <inheritdoc />
    public async Task RevokeActiveAsync(string userId, AccountSessionRevocationReason reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        Func<AccountSessionRevocationReason, Task>[] callbacks;
        lock (_sync)
        {
            callbacks = _subscribers.TryGetValue(userId, out var accountSubscribers)
                ? accountSubscribers.Values.ToArray()
                : [];
        }

        List<Exception>? failures = null;
        foreach (var callback in callbacks)
        {
            try
            {
                await callback(reason);
            }
            catch (ObjectDisposedException)
            {
                // A circuit can end after the snapshot and before its renderer callback runs.
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more account-session revocation callbacks failed.", failures);
        }
    }

    private void Unsubscribe(string userId, long id)
    {
        lock (_sync)
        {
            if (!_subscribers.TryGetValue(userId, out var accountSubscribers))
            {
                return;
            }

            accountSubscribers.Remove(id);
            if (accountSubscribers.Count == 0)
            {
                _subscribers.Remove(userId);
            }
        }
    }

    private sealed class Subscription(AccountSessionRevocationNotifier notifier, string userId, long id) : IDisposable
    {
        private AccountSessionRevocationNotifier? _notifier = notifier;

        public void Dispose() =>
            Interlocked.Exchange(ref _notifier, null)?.Unsubscribe(userId, id);
    }
}
