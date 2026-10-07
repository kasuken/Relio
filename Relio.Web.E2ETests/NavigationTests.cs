using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public class NavigationTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData("People", "/people", "People")] // the demo account has people, so the list - not the empty state
    [InlineData("Reminders", "/reminders", "Reminders")]
    [InlineData("Difficult moments", "/difficult-moments", "No difficult moments recorded")]
    [InlineData("Settings", "/settings", "Settings")]
    [InlineData("Administration", "/admin/users", "Administration")] // the demo account is an Administrator
    public async Task Drawer_link_navigates_to_its_page(string linkText, string expectedPath, string expectedHeading)
    {
        var page = await fixture.NewPageAsync();

        await RelioAppFixture.SignInAsDemoAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");

        await page.Locator("nav[aria-label='Primary']").GetByText(linkText, new() { Exact = true }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"{Regex.Escape(expectedPath)}$"));
        // By heading role, not by text: the drawer link carries the same words as the page title.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = expectedHeading, Exact = true })).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }
}
