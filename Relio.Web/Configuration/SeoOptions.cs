namespace Relio.Web.Configuration;

/// <summary>Configures canonical URLs for public metadata without trusting request host headers.</summary>
public sealed class SeoOptions
{
    /// <summary>The configuration section containing these options.</summary>
    public const string SectionName = "Seo";

    /// <summary>Gets or sets the public HTTPS origin used to build canonical URLs.</summary>
    public string? PublicOrigin { get; set; }

    /// <summary>Gets or sets the fixed site name used in title and social metadata.</summary>
    public string SiteName { get; set; } = "Relio";

    /// <summary>Builds the canonical URL for a fixed public policy route.</summary>
    /// <param name="kind">The policy kind.</param>
    /// <returns>An absolute canonical URL based only on configured origin and the fixed route.</returns>
    public string GetCanonicalUrl(PolicyDocumentKind kind) => GetCanonicalUrl(kind.GetRoute());

    /// <summary>
    /// Builds the canonical URL for an application-owned absolute path. Callers must not supply
    /// user-derived paths or values.
    /// </summary>
    /// <param name="applicationOwnedPath">A fixed, local application route beginning with one slash.</param>
    /// <returns>An absolute canonical URL based only on configured origin and the fixed route.</returns>
    /// <exception cref="ArgumentException">The path is not a safe local application route.</exception>
    /// <exception cref="InvalidOperationException">No public origin is configured.</exception>
    public string GetCanonicalUrl(string applicationOwnedPath)
    {
        ArgumentNullException.ThrowIfNull(applicationOwnedPath);
        if (applicationOwnedPath.Length == 0
            || applicationOwnedPath[0] != '/'
            || applicationOwnedPath.StartsWith("//", StringComparison.Ordinal)
            || applicationOwnedPath.Contains('\\')
            || applicationOwnedPath.Contains('?')
            || applicationOwnedPath.Contains('#')
            || applicationOwnedPath.Split('/').Any(segment => segment is "." or "..")
            || applicationOwnedPath.Any(char.IsControl))
        {
            throw new ArgumentException("A canonical URL path must be a fixed local application route.", nameof(applicationOwnedPath));
        }

        if (string.IsNullOrWhiteSpace(PublicOrigin))
        {
            throw new InvalidOperationException(
                $"{SectionName}:PublicOrigin is required to build a public canonical URL.");
        }

        return new Uri(new Uri(PublicOrigin, UriKind.Absolute), applicationOwnedPath).AbsoluteUri;
    }

    /// <summary>Builds the canonical URL for the application's sitemap endpoint.</summary>
    /// <returns>An absolute sitemap URL based only on the configured origin.</returns>
    /// <exception cref="InvalidOperationException">No public origin is configured.</exception>
    public string GetSitemapUrl()
    {
        if (string.IsNullOrWhiteSpace(PublicOrigin))
        {
            throw new InvalidOperationException(
                $"{SectionName}:PublicOrigin is required to build a public sitemap URL.");
        }

        return new Uri(new Uri(PublicOrigin, UriKind.Absolute), "/sitemap.xml").AbsoluteUri;
    }
}
