using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Relio.Data.Concurrency;

/// <summary>Marks a data service instance whose every call runs in its context's <see cref="DatabaseLane"/>.</summary>
public interface IDatabaseLaneProxy;

/// <summary>
/// Wraps a data service so each call through <typeparamref name="TService"/> runs alone on its
/// <see cref="RelioDbContext"/>. Created by <c>AddRelioData</c> for every data service; features
/// never use it directly. Public and unsealed because <see cref="DispatchProxy"/> requires it.
/// </summary>
/// <remarks>
/// <para>
/// The service interface may only declare methods returning <see cref="Task"/> or
/// <see cref="Task{TResult}"/> (checked by <see cref="ThrowIfUnsupported"/> at startup), so every
/// call can be queued without blocking a thread.
/// </para>
/// <para>
/// The method's own <see cref="CancellationToken"/> (the first one in its arguments) cancels the wait
/// for the lane, and is still passed on to the implementation. Exceptions surface as their own type
/// (not <see cref="TargetInvocationException"/>), always through the returned task.
/// </para>
/// </remarks>
/// <typeparam name="TService">The data service interface.</typeparam>
public class DatabaseLaneProxy<TService> : DispatchProxy, IDatabaseLaneProxy
    where TService : class
{
    private static readonly MethodInfo RunWithResultDefinition = typeof(DatabaseLaneProxy<TService>)
        .GetMethod(nameof(RunWithResult), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly ConcurrentDictionary<Type, MethodInfo> RunWithResultByType = new();

    private TService _inner = null!;
    private DatabaseLane _lane = null!;

    /// <summary>Throws unless <typeparamref name="TService"/> is an interface whose members are all methods returning Task or Task of T.</summary>
    /// <exception cref="InvalidOperationException">The interface cannot run in the database lane.</exception>
    public static void ThrowIfUnsupported()
    {
        var type = typeof(TService);
        if (!type.IsInterface)
        {
            throw new InvalidOperationException($"{type.Name} must be an interface to run in the database lane.");
        }

        var interfaces = new[] { type }.Concat(type.GetInterfaces()).ToList();
        var properties = interfaces.SelectMany(i => i.GetProperties()).Select(p => p.Name);
        var events = interfaces.SelectMany(i => i.GetEvents()).Select(e => e.Name);
        var unsupportedMethods = interfaces.SelectMany(i => i.GetMethods())
            .Where(m => !m.IsSpecialName && !IsTaskType(m.ReturnType))
            .Select(m => m.Name);

        var unsupported = properties.Concat(events).Concat(unsupportedMethods).ToList();
        if (unsupported.Count > 0)
        {
            throw new InvalidOperationException(
                $"{type.Name} cannot run in the database lane: {string.Join(", ", unsupported)} must be " +
                "methods returning Task or Task<T>.");
        }
    }

    /// <summary>Wraps <paramref name="inner"/> so each of its calls runs in <paramref name="lane"/>.</summary>
    public static TService Create(TService inner, DatabaseLane lane)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(lane);

        var proxy = Create<TService, DatabaseLaneProxy<TService>>();
        var self = (DatabaseLaneProxy<TService>)(object)proxy;
        self._inner = inner;
        self._lane = lane;
        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var cancellationToken = args?.OfType<CancellationToken>().FirstOrDefault() ?? default;

        if (targetMethod.ReturnType == typeof(Task))
        {
            return _lane.RunAsync(() => (Task)InvokeInner(targetMethod, args), cancellationToken);
        }

        var run = RunWithResultByType.GetOrAdd(
            targetMethod.ReturnType.GenericTypeArguments[0],
            resultType => RunWithResultDefinition.MakeGenericMethod(resultType));
        return run.Invoke(this, [targetMethod, args, cancellationToken]);
    }

    private static bool IsTaskType(Type type) =>
        type == typeof(Task) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>));

    private Task<T> RunWithResult<T>(MethodInfo targetMethod, object?[]? args, CancellationToken cancellationToken) =>
        _lane.RunAsync(() => (Task<T>)InvokeInner(targetMethod, args), cancellationToken);

    private object InvokeInner(MethodInfo targetMethod, object?[]? args)
    {
        try
        {
            return targetMethod.Invoke(_inner, args)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // Surface the service's own exception type (e.g. a validation exception), not the
            // reflection wrapper, so callers' catch blocks keep working.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw; // unreachable
        }
    }
}
