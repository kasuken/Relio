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
        if (options.PublicOrigin is not null
            && !PublicUrlValidation.IsSafeHttpUrl(
                options.PublicOrigin,
                requireHttps: !_environment.IsDevelopment(),
                originOnly: true))
        {
            failures.Add(
                $"{SeoOptions.SectionName}:PublicOrigin must be an HTTP(S) origin without credentials, path, query, or fragment; HTTPS is required outside Development.");
        }

        if (options.IndexingEnabled && string.IsNullOrWhiteSpace(options.PublicOrigin))
        {
            failures.Add($"{SeoOptions.SectionName}:PublicOrigin is required when indexing is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Description)
            || options.Description.Length > 240
            || options.Description.Any(char.IsControl))
        {
            failures.Add($"{SeoOptions.SectionName}:Description must be a non-empty value of at most 240 characters.");
        }

        try
        {
            new SeoOptions().GetCanonicalUrl(options.SocialPreviewPath);
            if (!options.SocialPreviewPath.StartsWith("/img/", StringComparison.Ordinal)
                || options.SocialPreviewPath.Contains('%')
                || !options.SocialPreviewPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{SeoOptions.SectionName}:SocialPreviewPath must be a local PNG under /img/.");
            }
        }
        catch (ArgumentException)
        {
            failures.Add($"{SeoOptions.SectionName}:SocialPreviewPath must be a fixed local asset path.");
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
