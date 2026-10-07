namespace Relio.Web.Configuration;

/// <summary>Configures canonical URLs for public metadata without trusting request host headers.</summary>
public sealed class SeoOptions
{
    /// <summary>The configuration section containing these options.</summary>
    public const string SectionName = "Seo";

    /// <summary>Safe local metadata origin used until an operator supplies their deployment URL.</summary>
    public const string DefaultPublicOrigin = "https://localhost";

    /// <summary>Gets or sets the explicit production crawler opt-in. Off by default.</summary>
    public bool IndexingEnabled { get; set; }

    /// <summary>Gets or sets the factual default description for the site.</summary>
    public string Description { get; set; } = "A private notebook for the people in your life.";

    /// <summary>Gets or sets a local social preview image path.</summary>
    public string SocialPreviewPath { get; set; } = "/img/og-preview.png";

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

        return new Uri(new Uri(PublicOrigin ?? DefaultPublicOrigin, UriKind.Absolute), applicationOwnedPath).AbsoluteUri;
    }

    /// <summary>Builds the canonical URL for the application's sitemap endpoint.</summary>
    /// <returns>An absolute sitemap URL based only on the configured origin.</returns>
    public string GetSitemapUrl() => GetCanonicalUrl("/sitemap.xml");
}
