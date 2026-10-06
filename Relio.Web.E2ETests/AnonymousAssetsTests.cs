using System.Net;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

/// <summary>
/// Regression tests for static assets under the fallback authorization policy (see AGENTS.md
/// "Protecting pages"): <c>MapStaticAssets()</c> must be <c>AllowAnonymous</c>, otherwise a
/// signed-out visitor's requests for the stylesheets and scripts are redirected to the login page
/// and the login/register pages render unstyled and without <c>blazor.web.js</c>.
/// </summary>
[Collection(RelioAppCollection.Name)]
public class AnonymousAssetsTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/Account/ForgotPassword")]
    public async Task Signed_out_visitor_gets_every_stylesheet_and_script_without_a_redirect(string path)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(path);

        var assetUrls = await page.EvaluateAsync<string[]>(
            """
            () => [
                ...document.querySelectorAll('link[rel="stylesheet"][href]'),
                ...document.querySelectorAll('script[src]'),
            ].map(e => e.href || e.src)
            """);

        assetUrls.Should().Contain(u => u.Contains("app", StringComparison.Ordinal) && u.EndsWith(".css", StringComparison.Ordinal));
        assetUrls.Should().Contain(u => u.Contains("_framework/blazor.web", StringComparison.Ordinal));
        assetUrls.Should().Contain(u => u.Contains("MudBlazor", StringComparison.Ordinal) && u.EndsWith(".css", StringComparison.Ordinal));

        foreach (var url in assetUrls)
        {
            // MaxRedirects = 0: a redirect to /Account/Login would be returned as a 302 instead of
            // being followed to a 200 HTML page that merely looks like a successful response.
            var response = await page.Context.APIRequest.GetAsync(url, new APIRequestContextOptions { MaxRedirects = 0 });

            response.Status.Should().Be((int)HttpStatusCode.OK, $"{url} must be served to anonymous visitors");
            response.Headers.GetValueOrDefault("content-type", "").Should().NotContain("text/html", $"{url} must be the asset, not the login page");
        }

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Login_page_is_styled_for_a_signed_out_visitor()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync("/Account/Login");

        // blazor.web.js loaded and started (enhanced navigation/forms are available)...
        await page.WaitForFunctionAsync("() => typeof window.Blazor !== 'undefined'");

        // ...and app.css applied: `.rl-field input` (wwwroot/app.css) gives the input a 1px border
        // and a rounded radius; the browser default is a 2px border with no radius.
        var input = page.Locator("[data-testid='login-email']");
        (await input.EvaluateAsync<string>("e => getComputedStyle(e).borderTopWidth")).Should().Be("1px");
        (await input.EvaluateAsync<string>("e => getComputedStyle(e).borderTopLeftRadius")).Should().NotBe("0px");

        await RelioAppFixture.ClosePageAsync(page);
    }
}
