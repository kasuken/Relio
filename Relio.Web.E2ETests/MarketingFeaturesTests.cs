using System.Net;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class MarketingFeaturesTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Features_render_anonymously_and_distinguish_shipped_from_planned_work()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        try
        {
            var response = await page.GotoAsync("/features");
            response!.Status.Should().Be((int)HttpStatusCode.OK);
            var initialHtml = await response.TextAsync();
            initialHtml.Should().Contain("Interactions and notes").And.Contain("not available yet")
                .And.NotContain("demo@relio.local").And.NotContain("\"type\":\"server\"");
            await Expect(page.Locator("h1")).ToHaveCountAsync(1);
            await Expect(page.Locator("link[rel='canonical']")).ToHaveAttributeAsync("href", "https://localhost/features");
            await Expect(page.Locator("#reflection-heading + p")).ToContainTextAsync("Planned, not available yet");
            await Expect(page.Locator("main")).ToContainTextAsync("searching people by name");
            await Expect(page.Locator("main")).ToContainTextAsync("Names, contact methods and indexed metadata remain readable");
            await Expect(page.Locator("main a[href='/Account/Register']").First)
                .ToHaveAttributeAsync("data-enhance-nav", "false");
            (await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth")).Should().BeTrue();
            await page.Locator("header a.rl-logo-link").ClickAsync();
            await Expect(page.Locator("main h1")).ToHaveTextAsync("A private notebook for your relationships");
            await Expect(page.Locator("link[rel='canonical']")).ToHaveAttributeAsync("href", "https://localhost/");
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }
}
