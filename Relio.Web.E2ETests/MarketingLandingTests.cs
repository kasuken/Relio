using System.Net;
using System.Text.Json;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class MarketingLandingTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData(360)]
    [InlineData(1440)]
    public async Task Landing_is_complete_responsive_and_uses_only_a_fictional_local_visual(int width)
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await page.SetViewportSizeAsync(width, 900);
            var response = await page.GotoAsync("/");
            response!.Status.Should().Be((int)HttpStatusCode.OK);
            await Expect(page.Locator("main h1")).ToHaveCountAsync(1);
            await Expect(page.Locator("main")).ToContainTextAsync("difficult moments and reflection");
            await Expect(page.Locator("main")).ToContainTextAsync("It is not available yet");
            await Expect(page.Locator("main figure")).ToContainTextAsync("fictional");
            (await page.Locator("main img").EvaluateAsync<bool>("img => img.complete && img.naturalWidth === 960"))
                .Should().BeTrue();
            (await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth")).Should().BeTrue();
            var json = await page.Locator("script[type='application/ld+json']").TextContentAsync();
            using var schema = JsonDocument.Parse(json!);
            schema.RootElement.GetProperty("@type").GetString().Should().Be("WebSite");
            schema.RootElement.TryGetProperty("offers", out _).Should().BeFalse();
            await page.GetByText("Do reminders need email?", new() { Exact = true }).ClickAsync();
            await Expect(page.Locator("details[open]")).ToContainTextAsync("configured mail delivery");
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }
}
