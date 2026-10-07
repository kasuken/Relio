using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public class DashboardTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Dashboard_loads_with_the_empty_state_when_user_has_no_people()
    {
        var page = await fixture.NewPageAsync();
        var email = $"dashboard-empty-{Guid.NewGuid():N}@example.com";

        await page.GotoAsync("/Account/Register");
        await page.Locator("[data-testid='register-email']").FillAsync(email);
        await page.Locator("[data-testid='register-password']").FillAsync("Str0ng-Passw0rd!");
        await page.Locator("[data-testid='register-confirm-password']").FillAsync("Str0ng-Passw0rd!");
        await page.Locator("[data-testid='register-submit']").ClickAsync();

        await page.GotoAsync("/dashboard");
        await page.Locator("html[data-app-ready='true']").WaitForAsync();

        await Expect(page.GetByText("No one here yet")).ToBeVisibleAsync();
        await Expect(
            page.GetByText("Relio is a private notebook about the people in your life. Adding people comes next."))
            .ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Dashboard_renders_active_dashboard_when_user_has_people()
    {
        var page = await fixture.NewPageAsync();

        await RelioAppFixture.SignInAsDemoAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dashboard-add-person']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dashboard-add-reminder']")).ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }
}
