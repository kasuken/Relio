using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Relio.Web.Configuration;

/// <summary>Validates the public source URL before Relio starts serving pages.</summary>
public sealed class SourceCodeOptionsValidator(IHostEnvironment environment) : IValidateOptions<SourceCodeOptions>
{
    private readonly IHostEnvironment _environment = environment
        ?? throw new ArgumentNullException(nameof(environment));

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SourceCodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var requireHttps = !_environment.IsDevelopment();
        return PublicUrlValidation.IsSafeHttpUrl(options.SourceCodeUrl, requireHttps)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{SourceCodeOptions.ConfigurationKey} must be an absolute HTTP(S) URL without credentials, query, or fragment; HTTPS is required outside Development.");
    }
}
