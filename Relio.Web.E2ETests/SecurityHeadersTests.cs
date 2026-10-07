using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using Relio.Web.Security;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class SecurityHeadersTests(RelioAppFixture fixture)
{
    private const string InlineScriptPattern =
        """<script\b(?<attributes>[^>]*)>(?<body>.*?)</script>""";

    [Fact]
    public async Task Headers_cover_redirect_health_static_asset_and_authenticated_not_found_responses()
    {
        using var client = CreateClient();

        using var redirect = await client.GetAsync("/settings");
        redirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        AssertSecurityHeaders(redirect);

        using var health = await client.GetAsync("/health/live");
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertSecurityHeaders(health);

        using var asset = await client.GetAsync("/_framework/blazor.web.js");
        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertSecurityHeaders(asset);

        var page = await fixture.NewPageAsync();
        try
        {
            await RelioAppFixture.SignInAsDemoAsync(page);
            var notFound = await page.GotoAsync("/security-header-not-found");
            notFound.Should().NotBeNull();
            notFound!.Status.Should().Be((int)HttpStatusCode.NotFound);
            await AssertSecurityHeadersAsync(notFound);
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Error_responses_also_receive_the_security_headers()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseRelioSecurityHeaders();
        app.UseExceptionHandler(errorApp => errorApp.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return context.Response.WriteAsync("A generic error occurred.");
        }));
        app.Run(_ => Task.FromException(new InvalidOperationException("synthetic-error-sentinel")));
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Registration_inline_script_nonce_matches_the_csp_and_changes_per_response()
    {
        using var client = CreateClient();

        using var first = await client.GetAsync("/Account/Register");
        var firstHtml = await first.Content.ReadAsStringAsync();
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstScripts = GetNonEmptyInlineScripts(firstHtml);
        var firstNonce = GetPolicyNonce(GetHeader(first, "Content-Security-Policy"));
        foreach (var inlineNonce in GetInlineScriptNonces(firstScripts))
        {
            inlineNonce.Should().Be(firstNonce);
        }

        firstScripts.Any(script =>
                script.Groups["attributes"].Value.Contains("importmap", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
        firstScripts.Any(script =>
                script.Groups["body"].Value.Contains("Intl.DateTimeFormat", StringComparison.Ordinal))
            .Should().BeTrue();

        using var second = await client.GetAsync("/Account/Register");
        var secondHtml = await second.Content.ReadAsStringAsync();
        var secondScripts = GetNonEmptyInlineScripts(secondHtml);
        var secondNonce = GetPolicyNonce(GetHeader(second, "Content-Security-Policy"));
        foreach (var inlineNonce in GetInlineScriptNonces(secondScripts))
        {
            inlineNonce.Should().Be(secondNonce);
        }

        secondNonce.Should().NotBe(firstNonce);
    }

    [Fact]
    public async Task Policy_blocks_inline_scripts_and_does_not_reflect_host_or_query_input()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/Account/Register?probe=security-policy-sentinel.example");
        request.Headers.Host = "host-sentinel.example";

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var policy = GetHeader(response, "Content-Security-Policy");
        var scriptSource = Regex.Match(
            policy,
            @"(?:^|;\s*)script-src\s+(?<sources>[^;]+)",
            RegexOptions.IgnoreCase).Groups["sources"].Value;

        scriptSource.Should().Contain("'self'");
        scriptSource.Should().Contain("'nonce-");
        scriptSource.Should().NotContain("'unsafe-inline'");
        scriptSource.Should().NotContain("'unsafe-eval'");
        policy.Should().Contain("frame-ancestors 'none'");
        policy.Should().Contain("object-src 'none'");
        policy.Should().Contain("base-uri 'self'");
        policy.Should().Contain("form-action 'self'");
        policy.Should().Contain("style-src 'self' 'unsafe-inline'");
        policy.Should().Contain("connect-src 'self'");
        policy.Should().NotContain("ws:");
        policy.Should().NotContain("wss:");
        policy.Should().NotContain("host-sentinel.example");
        policy.Should().NotContain("security-policy-sentinel.example");
    }

    private HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    })
    {
        BaseAddress = new Uri(fixture.BaseUrl),
    };

    private static Match[] GetNonEmptyInlineScripts(string html) =>
        Regex.Matches(
                html,
                InlineScriptPattern,
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Cast<Match>()
            .Where(match => !string.IsNullOrWhiteSpace(match.Groups["body"].Value))
            .ToArray();

    private static string[] GetInlineScriptNonces(IEnumerable<Match> scripts) =>
        scripts.Select(script =>
        {
            var nonce = Regex.Match(
                script.Groups["attributes"].Value,
                "\\bnonce=\"(?<nonce>[^\"]+)\"",
                RegexOptions.IgnoreCase);
            if (!nonce.Success)
            {
                throw new InvalidOperationException("An inline script did not contain a response nonce.");
            }

            return WebUtility.HtmlDecode(nonce.Groups["nonce"].Value);
        }).ToArray();

    private static string GetPolicyNonce(string policy)
    {
        var scriptSource = Regex.Match(
            policy,
            @"(?:^|;\s*)script-src\s+(?<sources>[^;]+)",
            RegexOptions.IgnoreCase).Groups["sources"].Value;
        return Regex.Match(scriptSource, @"'nonce-(?<nonce>[^']+)'").Groups["nonce"].Value;
    }

    private static string GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.Single()
            : throw new InvalidOperationException($"The response did not include the {name} header.");

    private static string GetHeader(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.Single(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        GetHeader(response, "X-Content-Type-Options").Should().Be("nosniff");
        GetHeader(response, "Referrer-Policy").Should().Be("no-referrer");
        GetHeader(response, "X-Frame-Options").Should().Be("DENY");
        GetHeader(response, "Content-Security-Policy").Should().Contain("frame-ancestors 'none'");
    }

    private static async Task AssertSecurityHeadersAsync(IResponse response)
    {
        var headers = await response.AllHeadersAsync();
        GetHeader(headers, "X-Content-Type-Options").Should().Be("nosniff");
        GetHeader(headers, "Referrer-Policy").Should().Be("no-referrer");
        GetHeader(headers, "X-Frame-Options").Should().Be("DENY");
        GetHeader(headers, "Content-Security-Policy").Should().Contain("frame-ancestors 'none'");
    }
}
