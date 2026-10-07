using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Relio.Web.Configuration;

/// <summary>
/// Validates review-gated policy configuration without echoing configured text or file paths.
/// </summary>
public sealed class HostedPoliciesOptionsValidator(
    IOptions<SeoOptions> seoOptions,
    IHostEnvironment environment) : IValidateOptions<HostedPoliciesOptions>
{
    private readonly IOptions<SeoOptions> _seoOptions = seoOptions
        ?? throw new ArgumentNullException(nameof(seoOptions));
    private readonly IHostEnvironment _environment = environment
        ?? throw new ArgumentNullException(nameof(environment));

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, HostedPoliciesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(_seoOptions.Value.PublicOrigin))
        {
            failures.Add($"{SeoOptions.SectionName}:PublicOrigin is required when policy hosting is enabled.");
        }

        var enabledDocuments = options.GetDocuments()
            .Where(document => document.Options?.Enabled == true)
            .ToArray();

        if (enabledDocuments.Length == 0)
        {
            failures.Add(
                $"{HostedPoliciesOptions.SectionName} must enable at least one document when its master switch is on.");
        }

        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var descriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, document) in enabledDocuments)
        {
            var section = $"{HostedPoliciesOptions.SectionName}:{GetSectionName(kind)}";
            if (document is null)
            {
                failures.Add($"{section} configuration is required for an enabled document.");
                continue;
            }

            if (!IsExternalContentFile(document.ContentFile))
            {
                failures.Add($"{section}:ContentFile must be an absolute path outside the application content root.");
            }

            if (!IsMetadataValue(document.Title, 100))
            {
                failures.Add($"{section}:Title must be non-empty and no longer than 100 characters.");
            }
            else if (!titles.Add(document.Title!))
            {
                failures.Add($"{section}:Title must be unique among enabled policy pages.");
            }

            if (!IsMetadataValue(document.Description, 240))
            {
                failures.Add($"{section}:Description must be non-empty and no longer than 240 characters.");
            }
            else if (!descriptions.Add(document.Description!))
            {
                failures.Add($"{section}:Description must be unique among enabled policy pages.");
            }

            if (!IsMetadataValue(document.Version, 64))
            {
                failures.Add($"{section}:Version must be non-empty and no longer than 64 characters.");
            }

            if (!document.HumanReviewAttested)
            {
                failures.Add(
                    $"{section}:HumanReviewAttested must be true only after a human has reviewed this document version.");
            }

            if (document.ReviewedAtUtc is null
                || document.ReviewedAtUtc.Value.Offset != TimeSpan.Zero)
            {
                failures.Add($"{section}:ReviewedAtUtc must be supplied in UTC.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private bool IsExternalContentFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            var contentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_environment.ContentRootPath));
            var fullPath = Path.GetFullPath(path);
            var relativePath = Path.GetRelativePath(contentRoot, fullPath);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return Path.IsPathRooted(relativePath)
                || string.Equals(relativePath, "..", comparison)
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", comparison)
                || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", comparison);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsMetadataValue(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && !value.Any(char.IsControl);

    private static string GetSectionName(PolicyDocumentKind kind) =>
        kind switch
        {
            PolicyDocumentKind.Privacy => "Privacy",
            PolicyDocumentKind.Terms => "Terms",
            PolicyDocumentKind.AcceptableUse => "AcceptableUse",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The policy document kind is not supported."),
        };
}
