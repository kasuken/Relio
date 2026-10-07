using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Relio.Web.Configuration;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class MarketingSeoTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Crawler_surface_requires_production_and_explicit_indexing(bool production, bool indexing)
    {
        await using var factory = new RelioWebAppFactory(services =>
        {
            services.AddSingleton<IHostEnvironment>(new SeoTestEnvironment(
                production ? Environments.Production : Environments.Development));
            services.PostConfigure<SeoOptions>(options =>
            {
                options.PublicOrigin = "https://relio.example";
                options.IndexingEnabled = indexing;
            });
        });
        _ = factory.Services;
        using var client = new HttpClient { BaseAddress = new Uri(factory.ServerAddress) };
        using var robots = await client.GetAsync("/robots.txt");
        robots.StatusCode.Should().Be(HttpStatusCode.OK);
        robots.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        var text = await robots.Content.ReadAsStringAsync();
        using var sitemap = await client.GetAsync("/sitemap.xml");
        if (production && indexing)
        {
            text.Should().Contain("Sitemap: https://relio.example/sitemap.xml")
                .And.Contain("Disallow: /Account/").And.NotContain("Allow: /dashboard");
            sitemap.StatusCode.Should().Be(HttpStatusCode.OK);
            sitemap.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            XDocument.Parse(await sitemap.Content.ReadAsStringAsync()).Descendants(ns + "loc")
                .Select(element => element.Value).Should().Equal(
                    "https://relio.example/", "https://relio.example/features",
                    "https://relio.example/pricing", "https://relio.example/changelog");
        }
        else
        {
            text.Should().Contain("Disallow: /").And.NotContain("Sitemap:");
            sitemap.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "attacker.example";
        using var landing = await client.SendAsync(request);
        var html = await landing.Content.ReadAsStringAsync();
        html.Should().Contain("https://relio.example/").And.NotContain("attacker.example");
        if (!production || !indexing)
        {
            landing.Headers.GetValues("X-Robots-Tag").Should().Contain("noindex, nofollow");
            html.Should().Contain("name=\"robots\" content=\"noindex, nofollow\"");
        }
    }

    [Fact]
    public async Task Default_metadata_and_social_asset_are_local_and_account_pages_are_nonindexable()
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        var html = await client.GetStringAsync("/");
        html.Should().Contain("<link rel=\"canonical\" href=\"https://localhost/\"")
            .And.Contain("property=\"og:image\" content=\"https://localhost/img/og-preview.png\"")
            .And.Contain("name=\"twitter:card\" content=\"summary_large_image\"");
        using var image = await client.GetAsync("/img/og-preview.png");
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        using var login = await client.GetAsync("/Account/Login");
        login.Headers.GetValues("X-Robots-Tag").Should().Contain("noindex, nofollow");
        (await login.Content.ReadAsStringAsync()).Should().Contain("name=\"robots\" content=\"noindex, nofollow\"");
    }

    private sealed class SeoTestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Relio.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
