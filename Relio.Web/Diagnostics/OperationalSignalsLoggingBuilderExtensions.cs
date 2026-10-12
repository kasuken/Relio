using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Relio.Web.Diagnostics;

/// <summary>Registers Relio's sanitized operational signals with the host logging builder.</summary>
public static class OperationalSignalsLoggingBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="CircuitFailureSignalLoggerProvider"/>, which reports unhandled Blazor circuit
    /// failures as <see cref="RelioOperationalEvents.CircuitFailure"/> while the framework categories
    /// stay suppressed for every other provider.
    /// </summary>
    /// <remarks>
    /// Call <see cref="PrivacyLoggingBuilderExtensions"/>' <c>AddRelioPrivacyLoggingFilters</c> as well; it
    /// supplies the filter rule that lets this provider see Error and above.
    /// </remarks>
    /// <param name="builder">The host logging builder.</param>
    /// <returns>The same logging builder.</returns>
    public static ILoggingBuilder AddRelioCircuitFailureSignal(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, CircuitFailureSignalLoggerProvider>());
        return builder;
    }
}
