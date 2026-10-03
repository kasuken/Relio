using static Microsoft.Playwright.Assertions;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public class DashboardTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Dashboard_loads_with_the_empty_state()
    {
        var page = await fixture.NewPageAsync();

        await RelioAppFixture.SignInAsDemoAsync(page);
        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/");

        await Expect(page.GetByText("No one here yet")).ToBeVisibleAsync();
        await Expect(
            page.GetByText("Relio is a private notebook about the people in your life. Adding people comes next."))
            .ToBeVisibleAsync();

        await RelioAppFixture.ClosePageAsync(page);
    }
}
