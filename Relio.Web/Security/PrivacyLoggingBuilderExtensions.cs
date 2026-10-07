using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Relio.Web.Security;

/// <summary>Registers code-enforced logging filters for framework categories that may expose private data.</summary>
public static class PrivacyLoggingBuilderExtensions
{
    private static readonly string[] SensitiveCategoryPrefixes =
    [
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware",
        "Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware",
        "Microsoft.AspNetCore.Components.Server",
        "Microsoft.AspNetCore.SignalR",
        "Microsoft.AspNetCore.Http.Connections",
        "Microsoft.AspNetCore.HttpLogging",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.EntityFrameworkCore.Database.Command",
        "Microsoft.EntityFrameworkCore.Database.Connection",
        "Microsoft.EntityFrameworkCore.Query",
        "Microsoft.EntityFrameworkCore.Update",
    ];

    /// <summary>
    /// Ensures sensitive ASP.NET Core, Blazor Server, SignalR, HTTP connection, and EF Core log
    /// categories remain disabled even when configuration or provider-specific rules enable them.
    /// </summary>
    /// <remarks>
    /// Add this after creating the host builder. Its post-configuration is reapplied when logging
    /// configuration reloads. Failures should be reported by application boundaries using generic
    /// event metadata and exception type only, never by attaching the exception object.
    /// </remarks>
    /// <param name="loggingBuilder">The host logging builder.</param>
    /// <returns>The same logging builder.</returns>
    public static ILoggingBuilder AddRelioPrivacyLoggingFilters(this ILoggingBuilder loggingBuilder)
    {
        ArgumentNullException.ThrowIfNull(loggingBuilder);

        loggingBuilder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            var providerNames = options.Rules
                .Select(rule => rule.ProviderName)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            for (var index = options.Rules.Count - 1; index >= 0; index--)
            {
                if (IsSensitiveCategory(options.Rules[index].CategoryName))
                {
                    options.Rules.RemoveAt(index);
                }
            }

            foreach (var categoryPrefix in SensitiveCategoryPrefixes)
            {
                options.Rules.Add(new LoggerFilterRule(
                    providerName: null,
                    categoryName: categoryPrefix,
                    logLevel: LogLevel.None,
                    filter: null));

                foreach (var providerName in providerNames)
                {
                    options.Rules.Add(new LoggerFilterRule(
                        providerName,
                        categoryPrefix,
                        LogLevel.None,
                        filter: null));
                }
            }
        });

        return loggingBuilder;
    }

    private static bool IsSensitiveCategory(string? categoryName) =>
        categoryName is not null
        && SensitiveCategoryPrefixes.Any(prefix =>
            string.Equals(categoryName, prefix, StringComparison.OrdinalIgnoreCase)
            || categoryName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
}
