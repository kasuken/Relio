using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public class NavigationTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData("People", "/people", "No one here yet")]
    [InlineData("Reminders", "/reminders", "No reminders yet")]
    [InlineData("Difficult moments", "/difficult-moments", "No difficult moments recorded")]
    [InlineData("Settings", "/settings", "Settings")]
    [InlineData("Administration", "/admin/users", "Administration")] // the demo account is an Administrator
    public async Task Drawer_link_navigates_to_its_page(string linkText, string expectedPath, string expectedHeading)
    {
        var page = await fixture.NewPageAsync();

        await RelioAppFixture.SignInAsDemoAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");

        await page.Locator("nav[aria-label='Primary']").GetByText(linkText, new() { Exact = true }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"{Regex.Escape(expectedPath)}$"));
        await Expect(page.GetByText(expectedHeading).First).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }
}
