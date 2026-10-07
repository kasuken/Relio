using System.Net;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class MarketingChangelogTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Changelog_is_public_static_and_readable_on_phone()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);
        try
        {
            var response = await page.GotoAsync("/changelog");
            response!.Status.Should().Be((int)HttpStatusCode.OK);
            (await response.TextAsync()).Should().Contain("Public marketing pages")
                .And.NotContain("\"type\":\"server\"");
            await Expect(page.Locator("h1")).ToHaveCountAsync(1);
            await Expect(page.Locator("main article")).ToContainTextAsync("Unreleased");
            await Expect(page.Locator("link[rel='canonical']")).ToHaveAttributeAsync("href", "https://localhost/changelog");
            (await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth")).Should().BeTrue();
            await Expect(page.Locator("main script, main iframe")).ToHaveCountAsync(0);
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }
}
