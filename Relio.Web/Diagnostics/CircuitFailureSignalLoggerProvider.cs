using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Relio.Web.Diagnostics;

/// <summary>
/// Turns the Blazor Server framework's circuit failures into one sanitized Relio operational event
/// (<see cref="RelioOperationalEvents.CircuitFailure"/>) without ever exposing what the framework logged.
/// </summary>
/// <remarks>
/// <para>
/// A render, lifecycle or event-handler exception reaches the framework's logger as a warning
/// ("Unhandled exception rendering component: {Message}") and then as an error on the
/// <c>CircuitHost</c> category ("Unhandled exception in circuit '{CircuitId}'.") before the circuit is
/// terminated. The first carries the exception message as a structured property, so even a formatted
/// line can contain private note, difficult-moment or form values. That is why every
/// <c>Microsoft.AspNetCore.Components.Server</c> category is forced to <see cref="LogLevel.None"/> for
/// all providers by the privacy filters, and why an unhandled circuit failure was invisible.
/// </para>
/// <para>
/// This provider is the single carve-out: <see cref="PrivacyLoggingBuilderExtensions"/> lets it, and
/// only it, receive <see cref="LogLevel.Error"/> and above from those categories. It never calls the
/// formatter and never reads the exception message, stack trace, data or inner exceptions or any state
/// value other than the circuit id (which it only emits as a one-way hash). A failed render produces one
/// warning (ignored, below Error) and one error, so exactly one signal is emitted per failure.
/// </para>
/// <para>
/// Debug-level interop and dispatch plumbing failures are intentionally not translated.
/// </para>
/// </remarks>
/// <param name="services">
/// The service provider, used to resolve <see cref="ILoggerFactory"/> lazily: the factory depends on
/// its providers, so taking it in the constructor would be circular.
/// </param>
[ProviderAlias(Alias)]
public sealed class CircuitFailureSignalLoggerProvider(IServiceProvider services) : ILoggerProvider
{
    /// <summary>The provider alias used in logging configuration and filter rules.</summary>
    public const string Alias = "RelioCircuitFailureSignal";

    /// <summary>The framework category prefix this provider watches.</summary>
    public const string WatchedCategoryPrefix = "Microsoft.AspNetCore.Components.Server";

    private ILogger? _signalLogger;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        IsWatched(categoryName) ? new SignalLogger(categoryName, this) : NullLogger.Instance;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>Whether a category belongs to the watched framework namespace.</summary>
    /// <param name="categoryName">The logger category.</param>
    private static bool IsWatched(string categoryName) =>
        string.Equals(categoryName, WatchedCategoryPrefix, StringComparison.OrdinalIgnoreCase)
        || categoryName.StartsWith(WatchedCategoryPrefix + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>Computes the short, one-way circuit reference used to correlate signals.</summary>
    /// <param name="circuitId">The framework's circuit id.</param>
    private static string HashCircuitId(string circuitId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("relio-circuit:" + circuitId));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    private void Emit(string category, EventId eventId, string circuitRef, Exception? exception)
    {
        var lastDot = category.LastIndexOf('.');
        var categoryName = lastDot < 0 ? category : category[(lastDot + 1)..];
        var eventName = eventId.Name ?? eventId.Id.ToString(CultureInfo.InvariantCulture);

        try
        {
            _signalLogger ??= services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(RelioOperationalEvents.CircuitFailureCategory);
            RelioOperationalEvents.CircuitFailure(
                _signalLogger,
                RelioOperationalEvents.ExceptionTypeOf(exception),
                $"{categoryName}.{eventName}",
                circuitRef);
        }
        catch (ObjectDisposedException)
        {
            // The framework still logs during host shutdown, after the container is disposed.
            // A logger must never throw.
        }
    }

    private sealed class SignalLogger(string category, CircuitFailureSignalLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Never call formatter and never read the exception beyond its type: see the class remarks.
            if (logLevel < LogLevel.Error)
            {
                return;
            }

            provider.Emit(category, eventId, CircuitRef(state), exception);
        }

        private static string CircuitRef<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (var (key, value) in properties)
                {
                    if (string.Equals(key, "CircuitId", StringComparison.Ordinal) && value is not null)
                    {
                        return HashCircuitId(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                    }
                }
            }

            return "none";
        }
    }
}
