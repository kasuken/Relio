using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Relio.Data.Seeding;
using Relio.Web.Configuration;
using Relio.Web.Tests.Infrastructure;

namespace Relio.Web.Tests.Marketing;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MarketingHttpCollection
{
    public const string Name = "Marketing HTTP";
}

[Collection(MarketingHttpCollection.Name)]
public sealed class MarketingHttpTests
{
    private const string Origin = "https://relio.example.test";
    private static readonly string[] MarketingRoutes = ["/", "/features", "/pricing", "/changelog"];

    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Production", true, true)]
    [InlineData("Production", false, true)]
    [InlineData("Development", true, true)]
    public async Task Public_initial_html_and_crawlers_match_real_environment_and_enabled_policies(
        string environment, bool indexing, bool policies)
    {
        await using var factory = CreateFactory(environment, indexing, policies);
        using var client = CreateClient(factory);
        using (var scope = factory.CreateRealScope())
        {
            scope.ServiceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName.Should().Be(environment);
        }
        var routes = policies
            ? MarketingRoutes.Concat(new[] { "/privacy", "/terms", "/acceptable-use" }).ToArray()
            : MarketingRoutes;
        var titles = new List<string>();
        var descriptions = new List<string>();
        var publicAssetPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in routes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Host = "attacker.example";
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await response.Content.ReadAsStringAsync();
            html.Should().NotContain("attacker.example").And.NotContain("demo@relio.local")
                .And.NotContain("Ada Lovelace").And.NotContain("\"type\":\"server\"");
            var document = new HtmlParser().ParseDocument(html);
            document.QuerySelectorAll("title").Should().HaveCount(1);
            document.QuerySelectorAll("h1").Should().HaveCount(1);
            document.QuerySelectorAll("main").Should().HaveCount(1);
            document.QuerySelectorAll("meta[name='description']").Should().HaveCount(1);
            document.QuerySelector("link[rel='canonical']")!.GetAttribute("href").Should().Be(Origin + route);
            document.QuerySelector("meta[property='og:url']")!.GetAttribute("content").Should().Be(Origin + route);
            document.QuerySelector("meta[property='og:image']")!.GetAttribute("content")
                .Should().Be(Origin + "/img/og-preview.png");
            titles.Add(document.QuerySelector("title")!.TextContent);
            descriptions.Add(document.QuerySelector("meta[name='description']")!.GetAttribute("content")!);
            document.QuerySelectorAll("link[rel='icon'][href], link[rel='apple-touch-icon'][href]")
                .Should().HaveCount(3, "every public page links the favicon and touch icons");
            foreach (var element in document.QuerySelectorAll(
                "link[rel='stylesheet'][href], link[rel='icon'][href], link[rel='apple-touch-icon'][href], script[src], img[src]"))
            {
                var asset = new Uri(client.BaseAddress!, element.GetAttribute("href") ?? element.GetAttribute("src")!);
                asset.Authority.Should().Be(client.BaseAddress!.Authority, "public assets must be self-hosted");
                publicAssetPaths.Add(asset.PathAndQuery);
            }
            if (environment != Environments.Production || !indexing)
            {
                document.QuerySelector("meta[name='robots']")!.GetAttribute("content").Should().Be("noindex, nofollow");
                response.Headers.GetValues("X-Robots-Tag").Should().Contain("noindex, nofollow");
            }
        }
        titles.Should().OnlyHaveUniqueItems();
        descriptions.Should().OnlyHaveUniqueItems();
        using var sitemap = await client.GetAsync("/sitemap.xml");
        var robots = await client.GetStringAsync("/robots.txt");
        if (environment == Environments.Production && indexing)
        {
            sitemap.StatusCode.Should().Be(HttpStatusCode.OK);
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            XDocument.Parse(await sitemap.Content.ReadAsStringAsync()).Descendants(ns + "loc")
                .Select(element => element.Value).Should().Equal(routes.Select(route => Origin + route));
            robots.Should().Contain("Allow: /$").And.Contain("Sitemap: " + Origin + "/sitemap.xml");
            foreach (var path in publicAssetPaths)
            {
                CrawlerAllows(robots, path).Should().BeTrue($"the actual public asset {path} must be crawlable");
                using var asset = await client.GetAsync(path);
                asset.StatusCode.Should().Be(HttpStatusCode.OK, $"the actual public asset {path} must be served");
            }
            foreach (var path in new[]
            {
                "/", "/features", "/pricing", "/changelog",
                "/app.abc.css", "/marketing.abc.css", "/Relio.Web.abc.styles.css",
                "/fonts/HankenGrotesk-Roman-latin.woff2", "/img/og-preview.png",
                "/favicon.ico", "/favicon.abc.ico", "/img/brand/apple-touch-icon.png",
                "/js/theme.abc.js", "/_framework/blazor.web.abc.js",
                "/_content/MudBlazor/MudBlazor.min.abc.css", "/Components/Layout/ReconnectModal.abc.razor.js",
            })
            {
                CrawlerAllows(robots, path).Should().BeTrue($"public page or asset {path} must be crawlable");
            }
            foreach (var path in new[] { "/dashboard", "/people", "/Account/Login", "/unsubscribe?token=secret", "/unknown", "/private.css" })
            {
                CrawlerAllows(robots, path).Should().BeFalse($"non-public route {path} must not be crawlable");
            }
        }
        else
        {
            sitemap.StatusCode.Should().Be(HttpStatusCode.NotFound);
            robots.Should().Be("User-agent: *\nDisallow: /\n");
            publicAssetPaths.Should().OnlyContain(path => !CrawlerAllows(robots, path));
        }
        foreach (var route in new[] { "/dashboard", "/people", "/settings", "/admin/users", "/onboarding" })
        {
            using var response = await client.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
            response.Headers.GetValues("X-Robots-Tag").Should().Contain("noindex, nofollow");
        }
        foreach (var route in new[] { "/Account/Login", "/Account/Register", "/Error", "/not-found" })
        {
            using var response = await client.GetAsync(route);
            response.Headers.GetValues("X-Robots-Tag").Should().Contain("noindex, nofollow");
            (await response.Content.ReadAsStringAsync()).Should().Contain("name=\"robots\" content=\"noindex, nofollow\"");
        }
        if (!policies)
        {
            using var response = await client.GetAsync("/privacy");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            response.Headers.GetValues("X-Robots-Tag").Should().Contain("noindex, nofollow");
        }
        using var image = await client.GetAsync("/img/og-preview.png");
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
    }

    [Fact]
    public async Task Structured_data_cannot_break_out_of_its_script_element()
    {
        await using var factory = new RelioWebAppFactory(services =>
        {
            services.PostConfigure<SeoOptions>(options =>
                options.SiteName = "Relio </script><script id='injected'>");
        });
        _ = factory.Services;
        using var client = CreateClient(factory);
        var document = new HtmlParser().ParseDocument(await client.GetStringAsync("/"));
        document.QuerySelector("#injected").Should().BeNull();
        var json = document.QuerySelector("script[type='application/ld+json']")!.TextContent;
        using var structuredData = System.Text.Json.JsonDocument.Parse(json);
        structuredData.RootElement.GetProperty("name").GetString().Should().Contain("</script>");
    }

    private static RelioWebAppFactory CreateFactory(string environment, bool indexing, bool policies)
    {
        var factory = new RelioWebAppFactory(services =>
        {
            services.PostConfigure<DemoDataOptions>(options => options.Enabled = false);
            services.PostConfigure<SeoOptions>(options =>
            {
                options.PublicOrigin = Origin;
                options.IndexingEnabled = indexing;
            });
            services.PostConfigure<HostedPoliciesOptions>(options =>
            {
                options.Enabled = policies;
                options.IndexingEnabled = false;
                options.Privacy = Policy("privacy", policies);
                options.Terms = Policy("terms", policies);
                options.AcceptableUse = Policy("acceptable-use", policies);
            });
        }, environmentName: environment);
        _ = factory.Services;
        return factory;
    }

    private static PolicyDocumentOptions Policy(string name, bool enabled) => new()
    {
        Enabled = enabled,
        ContentFile = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Policies", name + ".txt"),
        Title = name + " policy test fixture",
        Description = "Synthetic reviewed " + name + " fixture, not real legal wording.",
        Version = "test-only-1",
        HumanReviewAttested = true,
        ReviewedAtUtc = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero),
    };

    private static HttpClient CreateClient(RelioWebAppFactory factory) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(factory.ServerAddress) };

    private static bool CrawlerAllows(string robots, string path)
    {
        var matches = robots.Split('\n')
            .Where(line => line.StartsWith("Allow: ", StringComparison.Ordinal) || line.StartsWith("Disallow: ", StringComparison.Ordinal))
            .Select(line => (Allow: line.StartsWith("Allow: ", StringComparison.Ordinal), Path: line[(line.IndexOf(' ') + 1)..]))
            .Where(rule => Regex.IsMatch(path,
                "^" + Regex.Escape(rule.Path).Replace("\\*", ".*", StringComparison.Ordinal)
                    .Replace("\\$", "$", StringComparison.Ordinal)))
            .OrderByDescending(rule => rule.Path.Replace("*", "", StringComparison.Ordinal).TrimEnd('$').Length)
            .ThenByDescending(rule => rule.Allow)
            .ToArray();
        return matches.Length == 0 || matches[0].Allow;
    }
}
