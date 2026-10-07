using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Relio.Web.Configuration;

namespace Relio.Web.Endpoints;

/// <summary>Maps anonymous crawler endpoints for enabled public policy pages.</summary>
public static class MarketingEndpointRouteBuilderExtensions
{
    private static readonly XNamespace SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>
    /// Maps <c>/robots.txt</c> and <c>/sitemap.xml</c>. Only Production with indexing explicitly
    /// enabled advertises a sitemap, and sitemap URLs are built from configured origin.
    /// </summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <returns>The same endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapRelioMarketingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            "/robots.txt",
            (
                IHostEnvironment environment,
                IOptions<HostedPoliciesOptions> options,
                IHostedPolicyDocumentProvider documents,
                IOptions<SeoOptions> seoOptions) =>
            {
                var enabledDocuments = documents.EnabledDocuments;
                var canIndex = CanIndex(environment, options.Value, enabledDocuments);
                var lines = new List<string>
                {
                    "User-agent: *",
                    "Disallow: /",
                };

                if (canIndex)
                {
                    lines.AddRange(enabledDocuments.Select(document => $"Allow: {document.CanonicalPath}"));
                    lines.Add($"Sitemap: {seoOptions.Value.GetSitemapUrl()}");
                }

                return Results.Text(string.Join('\n', lines) + "\n", "text/plain; charset=utf-8");
            })
            .AllowAnonymous();

        endpoints.MapGet(
            "/sitemap.xml",
            (
                HttpContext httpContext,
                IHostEnvironment environment,
                IOptions<HostedPoliciesOptions> options,
                IHostedPolicyDocumentProvider documents,
                IOptions<SeoOptions> seoOptions) =>
            {
                var enabledDocuments = documents.EnabledDocuments;
                if (!CanIndex(environment, options.Value, enabledDocuments))
                {
                    var statusCodePages = httpContext.Features.Get<IStatusCodePagesFeature>();
                    if (statusCodePages is not null)
                    {
                        statusCodePages.Enabled = false;
                    }

                    return Results.NotFound();
                }

                return Results.Text(
                    BuildSitemap(enabledDocuments, seoOptions.Value),
                    "application/xml; charset=utf-8");
            })
            .AllowAnonymous();

        return endpoints;
    }

    private static bool CanIndex(
        IHostEnvironment environment,
        HostedPoliciesOptions options,
        IReadOnlyList<HostedPolicyDocument> documents) =>
        environment.IsProduction()
        && options.Enabled
        && options.IndexingEnabled
        && documents.Count > 0;

    private static string BuildSitemap(
        IReadOnlyList<HostedPolicyDocument> documents,
        SeoOptions seoOptions)
    {
        var urlElements = documents.Select(document =>
            new XElement(
                SitemapNamespace + "url",
                new XElement(SitemapNamespace + "loc", seoOptions.GetCanonicalUrl(document.Kind))));

        var sitemap = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(SitemapNamespace + "urlset", urlElements));

        return sitemap.ToString(SaveOptions.DisableFormatting);
    }
}
