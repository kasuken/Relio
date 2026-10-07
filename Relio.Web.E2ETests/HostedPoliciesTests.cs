using System.Collections.Concurrent;
using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Data.Seeding;
using Relio.Web.Configuration;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class HostedPoliciesTests(RelioAppFixture fixture)
{
    private const string PublicOrigin = "https://relio.example.test";
    private static readonly DateTimeOffset ReviewAttestation = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    private static readonly PolicyFixture[] AllPolicies =
    [
        new(
            PolicyDocumentKind.Privacy,
            "Privacy test fixture",
            "Synthetic privacy fixture metadata for rendering tests.",
            "privacy.txt",
            "SYNTHETIC REVIEWED TEST FIXTURE"),
        new(
            PolicyDocumentKind.Terms,
            "Terms test fixture",
            "Synthetic terms fixture metadata for rendering tests.",
            "terms.txt",
            "TEST-TERMS-ONLY"),
        new(
            PolicyDocumentKind.AcceptableUse,
            "Acceptable-use test fixture",
            "Synthetic acceptable-use fixture metadata for rendering tests.",
            "acceptable-use.txt",
            "TEST-ACCEPTABLE-USE-ONLY"),
    ];

    [Fact]
    public async Task Default_instance_keeps_policy_routes_off_and_source_code_link_visible()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        try
        {
            await page.GotoAsync("/Account/Login");
            (await page.GetByRole(AriaRole.Link, new() { Name = "Source code", Exact = true }).IsVisibleAsync())
                .Should().BeTrue();
            (await page.Locator("a[href='/privacy'], a[href='/terms'], a[href='/acceptable-use']").CountAsync())
                .Should().Be(0);

            await page.GotoAsync("/Account/Register");
            (await page.GetByRole(AriaRole.Link, new() { Name = "Source code", Exact = true }).IsVisibleAsync())
                .Should().BeTrue();
            (await page.Locator("a[href='/privacy'], a[href='/terms'], a[href='/acceptable-use']").CountAsync())
                .Should().Be(0);

            var disabledPolicy = await page.GotoAsync("/privacy");
            disabledPolicy.Should().NotBeNull();
            disabledPolicy!.Status.Should().Be((int)HttpStatusCode.NotFound);
            page.Url.Should().EndWith("/privacy");
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Source code", Exact = true }))
                .ToBeVisibleAsync();
            await Expect(page.GetByTestId("policy-document")).ToHaveCountAsync(0);
            (await page.GetByRole(AriaRole.Link, new() { Name = "Source code", Exact = true }).IsVisibleAsync())
                .Should().BeTrue();
            (await page.Locator("a[href='/privacy'], a[href='/terms'], a[href='/acceptable-use']").CountAsync())
                .Should().Be(0);

            using var client = CreateAnonymousClient(fixture.BaseUrl);
            using var robotsResponse = await client.GetAsync("/robots.txt");
            robotsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var robots = await robotsResponse.Content.ReadAsStringAsync();
            robots.Should().NotContain("Sitemap:");

            using var sitemapResponse = await client.GetAsync("/sitemap.xml");
            sitemapResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            await page.GotoAsync("/");
            new Uri(page.Url).AbsolutePath.Should().Be("/Account/Login");

            await RelioAppFixture.SignInAsDemoAsync(page);
            await page.GotoAsync("/");
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            page.Url.Should().EndWith("/");
            (await page.GetByRole(AriaRole.Link, new() { Name = "Source code", Exact = true }).IsVisibleAsync())
                .Should().BeTrue();
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Enabled_policy_pages_render_unique_safe_metadata_on_phone_and_desktop()
    {
        await using var app = CreatePolicyFactory(
            production: false,
            indexingEnabled: true,
            enabledKinds: AllPolicies.Select(policy => policy.Kind));

        var appOrigin = new Uri(app.ServerAddress).Authority;
        foreach (var viewport in new[] { Viewports.Phone, Viewports.Desktop })
        {
            var page = await fixture.NewPageAsync(viewport);
            var externalRequests = new ConcurrentBag<string>();
            page.Request += (_, request) =>
            {
                if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
                    && uri.Scheme is "http" or "https"
                    && !string.Equals(uri.Authority, appOrigin, StringComparison.OrdinalIgnoreCase))
                {
                    externalRequests.Add(request.Url);
                }
            };

            try
            {
                var titles = new List<string>();
                var descriptions = new List<string>();
                var canonicalUrls = new List<string>();

                foreach (var policy in AllPolicies)
                {
                    var response = await page.GotoAsync($"{app.ServerAddress}{policy.Kind.GetRoute()}");
                    response.Should().NotBeNull();
                    response!.Status.Should().Be((int)HttpStatusCode.OK);

                    var expectedTitle = $"{policy.Title} - Relio";
                    (await page.TitleAsync()).Should().Be(expectedTitle);
                    expectedTitle.Should().NotContain(DemoDataSeeder.DemoEmail);
                    expectedTitle.Should().NotContain("Ada Lovelace");
                    (await page.Locator("h1").CountAsync()).Should().Be(1);
                    (await page.Locator("h1").InnerTextAsync()).Should().Be(policy.Title);
                    (await page.Locator("meta[name='description']").GetAttributeAsync("content"))
                        .Should().Be(policy.Description);
                    policy.Description.Should().NotContain(DemoDataSeeder.DemoEmail);
                    policy.Description.Should().NotContain("Ada Lovelace");

                    var canonicalUrl = $"{PublicOrigin}{policy.Kind.GetRoute()}";
                    (await page.Locator("link[rel='canonical']").GetAttributeAsync("href"))
                        .Should().Be(canonicalUrl);
                    (await page.Locator("meta[property='og:url']").GetAttributeAsync("content"))
                        .Should().Be(canonicalUrl);
                    (await page.Locator("meta[property='og:title']").GetAttributeAsync("content"))
                        .Should().Be(expectedTitle);
                    (await page.Locator("meta[property='og:description']").GetAttributeAsync("content"))
                        .Should().Be(policy.Description);

                    var text = await page.Locator(".rl-policy-copy").InnerTextAsync();
                    text.Should().Contain(policy.BodyMarker);
                    text.Should().Contain("no legal approval is claimed");
                    text.Should().NotContain(DemoDataSeeder.DemoEmail);
                    text.Should().NotContain("Ada Lovelace");
                    (await page.Locator(".rl-policy-copy script").CountAsync()).Should().Be(0);
                    (await page.EvaluateAsync<bool>(
                            "() => window.relioPolicyFixtureInjected === true"))
                        .Should().BeFalse();

                    (await page.GetByRole(AriaRole.Link, new() { Name = "Source code", Exact = true }).IsVisibleAsync())
                        .Should().BeTrue();
                    (await page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true }).IsVisibleAsync())
                        .Should().BeTrue();
                    (await page.Locator("nav[aria-label='Policies for this instance'] a").CountAsync())
                        .Should().Be(3);
                    (await page.EvaluateAsync<bool>(
                            "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"))
                        .Should().BeTrue();

                    titles.Add(await page.TitleAsync());
                    descriptions.Add(policy.Description);
                    canonicalUrls.Add(canonicalUrl);
                }

                titles.Distinct(StringComparer.Ordinal).Should().HaveCount(AllPolicies.Length);
                descriptions.Distinct(StringComparer.Ordinal).Should().HaveCount(AllPolicies.Length);
                canonicalUrls.Distinct(StringComparer.Ordinal).Should().HaveCount(AllPolicies.Length);
                externalRequests.Should().BeEmpty();
            }
            finally
            {
                await RelioAppFixture.ClosePageAsync(page);
            }
        }

        using var developmentClient = CreateAnonymousClient(app.ServerAddress);
        using var developmentRobotsResponse = await developmentClient.GetAsync("/robots.txt");
        developmentRobotsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await developmentRobotsResponse.Content.ReadAsStringAsync()).Should().NotContain("Sitemap:");
        using var developmentSitemapResponse = await developmentClient.GetAsync("/sitemap.xml");
        developmentSitemapResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var hostHeaderClient = CreateAnonymousClient(app.ServerAddress);
        using var hostileHostRequest = new HttpRequestMessage(HttpMethod.Get, "/privacy");
        hostileHostRequest.Headers.Host = "attacker.example";
        using var hostileHostResponse = await hostHeaderClient.SendAsync(hostileHostRequest);
        hostileHostResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var hostileHostHtml = await hostileHostResponse.Content.ReadAsStringAsync();
        hostileHostHtml.Should().Contain($"{PublicOrigin}/privacy");
        hostileHostHtml.Should().NotContain("attacker.example");
    }

    [Fact]
    public async Task Registration_policy_links_remain_full_navigation_and_keep_the_browser_timezone_on_post()
    {
        await using var app = CreatePolicyFactory(
            production: false,
            indexingEnabled: false,
            enabledKinds: AllPolicies.Select(policy => policy.Kind));

        var page = await fixture.NewPageAsync(Viewports.Desktop, timezoneId: "Pacific/Kiritimati");
        try
        {
            await page.GotoAsync($"{app.ServerAddress}/Account/Register");
            (await page.GetByRole(AriaRole.Link, new() { Name = "Source code", Exact = true }).IsVisibleAsync())
                .Should().BeTrue();

            var privacyLink = page.Locator(".rl-registration-policy-links a[href='/privacy']");
            (await privacyLink.GetAttributeAsync("data-enhance-nav")).Should().Be("false");
            await privacyLink.ClickAsync();
            await page.Locator("[data-testid='policy-document']").WaitForAsync();
            page.Url.Should().EndWith("/privacy");

            await page.GoBackAsync();
            await page.Locator("#register-timezone-input").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            (await page.Locator("#register-timezone-input").InputValueAsync()).Should().Be("Pacific/Kiritimati");

            var email = AccountTestHelpers.NewEmail("policy-hosting");
            await page.Locator("[data-testid='register-email']").FillAsync(email);
            await page.Locator("[data-testid='register-password']").FillAsync(AccountTestHelpers.StrongPassword);
            await page.Locator("[data-testid='register-confirm-password']").FillAsync(AccountTestHelpers.StrongPassword);
            await page.Locator("[data-testid='register-submit']").ClickAsync();
            await page.Locator("[data-testid='onboarding-page'], [data-testid='register-confirmation-heading']")
                .WaitForAsync();

            using var scope = app.CreateRealScope();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>()
                .FindByEmailAsync(email);
            user.Should().NotBeNull();
            var profile = await scope.ServiceProvider.GetRequiredService<RelioDbContext>()
                .UserProfiles
                .AsNoTracking()
                .SingleAsync(row => row.OwnerId == user!.Id);
            profile.TimeZoneId.Should().Be("Pacific/Kiritimati");
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Production_sitemap_contains_only_enabled_policy_routes_from_configured_origin()
    {
        var enabledKinds = new[] { PolicyDocumentKind.Privacy, PolicyDocumentKind.Terms };
        await using var app = CreatePolicyFactory(
            production: true,
            indexingEnabled: true,
            enabledKinds);

        using var client = CreateAnonymousClient(app.ServerAddress);
        using var robotsResponse = await client.GetAsync("/robots.txt");
        robotsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var robots = await robotsResponse.Content.ReadAsStringAsync();
        robots.Should().Contain($"Sitemap: {PublicOrigin}/sitemap.xml");
        robots.Should().Contain("Allow: /privacy");
        robots.Should().Contain("Allow: /terms");
        robots.Should().NotContain("/acceptable-use");

        using var sitemapResponse = await client.GetAsync("/sitemap.xml");
        sitemapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sitemapText = await sitemapResponse.Content.ReadAsStringAsync();
        var sitemap = XDocument.Parse(sitemapText);
        XNamespace sitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = sitemap
            .Descendants(sitemapNamespace + "loc")
            .Select(location => location.Value)
            .ToArray();

        locations.Should().Equal(
            $"{PublicOrigin}/privacy",
            $"{PublicOrigin}/terms");

        await using var indexingDisabledApp = CreatePolicyFactory(
            production: true,
            indexingEnabled: false,
            enabledKinds);
        using var indexingDisabledClient = CreateAnonymousClient(indexingDisabledApp.ServerAddress);
        using var indexingDisabledRobotsResponse = await indexingDisabledClient.GetAsync("/robots.txt");
        indexingDisabledRobotsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await indexingDisabledRobotsResponse.Content.ReadAsStringAsync()).Should().NotContain("Sitemap:");
        using var indexingDisabledSitemapResponse = await indexingDisabledClient.GetAsync("/sitemap.xml");
        indexingDisabledSitemapResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static RelioWebAppFactory CreatePolicyFactory(
        bool production,
        bool indexingEnabled,
        IEnumerable<PolicyDocumentKind> enabledKinds)
    {
        var enabled = enabledKinds.ToHashSet();
        var factory = new RelioWebAppFactory(services =>
        {
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(
                production ? Environments.Production : Environments.Development,
                Path.Combine(AppContext.BaseDirectory, "policy-hosting-content-root")));

            services.PostConfigure<SeoOptions>(options =>
            {
                options.PublicOrigin = PublicOrigin;
                options.SiteName = "Relio";
            });

            services.PostConfigure<HostedPoliciesOptions>(options =>
            {
                options.Enabled = true;
                options.IndexingEnabled = indexingEnabled;
                options.Privacy = CreatePolicyOptions(PolicyDocumentKind.Privacy, enabled);
                options.Terms = CreatePolicyOptions(PolicyDocumentKind.Terms, enabled);
                options.AcceptableUse = CreatePolicyOptions(PolicyDocumentKind.AcceptableUse, enabled);
            });
        });

        _ = factory.Services;
        return factory;
    }

    private static PolicyDocumentOptions CreatePolicyOptions(
        PolicyDocumentKind kind,
        IReadOnlySet<PolicyDocumentKind> enabledKinds)
    {
        var policy = AllPolicies.Single(policy => policy.Kind == kind);
        return new PolicyDocumentOptions
        {
            Enabled = enabledKinds.Contains(kind),
            ContentFile = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Policies",
                policy.FileName),
            Title = policy.Title,
            Description = policy.Description,
            Version = "test-fixture-1",
            HumanReviewAttested = true,
            ReviewedAtUtc = ReviewAttestation,
        };
    }

    private static HttpClient CreateAnonymousClient(string baseAddress) =>
        new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseAddress),
        };

    private sealed record PolicyFixture(
        PolicyDocumentKind Kind,
        string Title,
        string Description,
        string FileName,
        string BodyMarker);

    private sealed class TestHostEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = typeof(HostedPoliciesTests).Assembly.GetName().Name!;

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
