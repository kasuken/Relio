using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Relio.Web.Configuration;

/// <summary>Validates canonical-origin and site-name configuration before Relio starts.</summary>
public sealed class SeoOptionsValidator(IHostEnvironment environment) : IValidateOptions<SeoOptions>
{
    private readonly IHostEnvironment _environment = environment
        ?? throw new ArgumentNullException(nameof(environment));

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SeoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.PublicOrigin)
            && !PublicUrlValidation.IsSafeHttpUrl(
                options.PublicOrigin,
                requireHttps: !_environment.IsDevelopment(),
                originOnly: true))
        {
            failures.Add(
                $"{SeoOptions.SectionName}:PublicOrigin must be an HTTP(S) origin without credentials, path, query, or fragment; HTTPS is required outside Development.");
        }

        if (string.IsNullOrWhiteSpace(options.SiteName)
            || options.SiteName.Length > 80
            || options.SiteName.Any(char.IsControl))
        {
            failures.Add($"{SeoOptions.SectionName}:SiteName must be a non-empty value of at most 80 characters.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
