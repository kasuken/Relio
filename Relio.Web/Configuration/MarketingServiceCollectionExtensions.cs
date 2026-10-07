using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Relio.Web.Configuration;

/// <summary>Registers validated source-link, SEO, and optional policy-hosting services.</summary>
public static class MarketingServiceCollectionExtensions
{
    /// <summary>
    /// Registers configuration validation and the startup-loaded policy document provider.
    /// The source-code link has a safe upstream default; policy hosting is disabled by default.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">The application's configuration root.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRelioMarketing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<SourceCodeOptions>, SourceCodeOptionsValidator>();
        services.AddOptions<SourceCodeOptions>()
            .Configure(options =>
            {
                options.SourceCodeUrl = configuration[SourceCodeOptions.ConfigurationKey]
                    ?? SourceCodeOptions.DefaultSourceCodeUrl;
            })
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<SeoOptions>, SeoOptionsValidator>();
        services.AddOptions<SeoOptions>()
            .Bind(configuration.GetSection(SeoOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<HostedPoliciesOptions>, HostedPoliciesOptionsValidator>();
        services.AddOptions<HostedPoliciesOptions>()
            .Bind(configuration.GetSection(HostedPoliciesOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<HostedPolicyDocumentProvider>();
        services.AddSingleton<IHostedPolicyDocumentProvider>(
            serviceProvider => serviceProvider.GetRequiredService<HostedPolicyDocumentProvider>());
        services.AddHostedService<HostedPolicyDocumentProvider>(
            serviceProvider => serviceProvider.GetRequiredService<HostedPolicyDocumentProvider>());

        return services;
    }
}
