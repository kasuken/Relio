using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;
using AspNetForwardedHeadersOptions = Microsoft.AspNetCore.Builder.ForwardedHeadersOptions;

namespace Relio.Web.Security;

/// <summary>Wires the forwarded-headers middleware so rate limiting and HTTPS redirection see the real client.</summary>
public static class ForwardedHeadersExtensions
{
    /// <summary>Binds and validates <see cref="ForwardedHeadersSettings"/> and configures the framework middleware.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRelioForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ForwardedHeadersSettings>()
            .Bind(configuration.GetSection(ForwardedHeadersSettings.SectionName))
            .Validate(
                settings => settings.ForwardLimit >= 1,
                "Security:ForwardedHeaders:ForwardLimit must be at least 1.")
            .Validate(
                settings => settings.KnownProxies.TrueForAll(value => IPAddress.TryParse(value, out _)),
                "Security:ForwardedHeaders:KnownProxies must contain only IP addresses.")
            .Validate(
                settings => settings.KnownNetworks.TrueForAll(value => IPNetwork.TryParse(value, out _)),
                "Security:ForwardedHeaders:KnownNetworks must contain only CIDR networks such as 10.0.0.0/8.")
            .ValidateOnStart();

        services.AddOptions<AspNetForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) =>
            {
                var value = settings.Value;
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = value.ForwardLimit;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();

                foreach (var proxy in value.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                foreach (var network in value.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(IPNetwork.Parse(network));
                }
            });

        return services;
    }

    /// <summary>
    /// Applies forwarded headers when <see cref="ForwardedHeadersSettings.Enabled"/> is set. Must be the
    /// first middleware so everything after it sees the client address and scheme.
    /// </summary>
    /// <param name="app">The application pipeline.</param>
    /// <returns>The same pipeline.</returns>
    public static IApplicationBuilder UseRelioForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;
        return settings.Enabled ? app.UseForwardedHeaders() : app;
    }
}
