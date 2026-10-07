using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public class ThemeTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Switching_to_dark_sets_data_theme_and_persists_across_reload()
    {
        var page = await fixture.NewPageAsync();

        await RelioAppFixture.SignInAsDemoAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");

        await page.Locator("button[aria-label='Change appearance']").ClickAsync();
        await page.GetByText("Dark", new() { Exact = true }).ClickAsync();

        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");

        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        await RelioAppFixture.ClosePageAsync(page);
    }
}
