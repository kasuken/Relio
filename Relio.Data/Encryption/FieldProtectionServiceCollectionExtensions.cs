using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Relio.Data.Encryption;

/// <summary>Registers the data-layer field-protection services.</summary>
public static class FieldProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared Data Protection field protector and the explicit startup backfill
    /// service. Call after <c>AddDataProtection</c> and before registering
    /// <see cref="RelioDbContext"/> with <c>AddRelioData</c>.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRelioFieldProtection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDataProtectionFieldProtector, DataProtectionFieldProtector>();
        services.TryAddScoped<SensitiveFieldBackfillService>();
        return services;
    }

    /// <summary>Compatibility alias for <see cref="AddRelioFieldProtection"/>.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddFieldProtection(this IServiceCollection services) =>
        AddRelioFieldProtection(services);
}
