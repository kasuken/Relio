using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public class ResponsiveDrawerTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task On_a_phone_viewport_the_drawer_is_hidden_until_opened_from_the_hamburger()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone);

        await RelioAppFixture.SignInAsDemoAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");

        var nav = page.Locator("nav[aria-label='Primary']");
        await Expect(nav).ToBeHiddenAsync();

        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Toggle navigation" }).ClickAsync();

        await Expect(nav).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task On_a_desktop_viewport_the_drawer_is_visible_without_opening_it()
    {
        var page = await fixture.NewPageAsync(Viewports.Desktop);

        await RelioAppFixture.SignInAsDemoAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");

        await Expect(page.Locator("nav[aria-label='Primary']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }
}
