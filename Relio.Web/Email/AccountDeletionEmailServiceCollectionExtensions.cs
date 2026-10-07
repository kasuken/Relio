using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Relio.Web.Email;

/// <summary>Registers the post-erasure notice using Relio's existing email-provider setting.</summary>
public static class AccountDeletionEmailServiceCollectionExtensions
{
    /// <summary>Adds the SMTP or no-provider account-deletion confirmation sender.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAccountDeletionConfirmationEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration[$"{EmailOptions.SectionName}:Provider"];
        if (string.Equals(provider, EmailOptions.SmtpProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IAccountDeletionConfirmationSender, SmtpAccountDeletionConfirmationSender>();
        }
        else
        {
            services.AddScoped<IAccountDeletionConfirmationSender, NoAccountDeletionConfirmationSender>();
        }

        return services;
    }
}
