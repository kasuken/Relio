using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;
using Relio.Data.Seeding;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public class AuthenticationTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Unauthenticated_visit_redirects_to_login()
    {
        var page = await fixture.NewPageAsync();

        await page.GotoAsync("/");

        await Expect(page).ToHaveURLAsync(new Regex("/Account/Login"));

        await RelioAppFixture.ClosePageAsync(page);
    }

    [Fact]
    public async Task Demo_user_can_sign_in_and_sees_the_app()
    {
        var page = await fixture.NewPageAsync();

        await RelioAppFixture.SignInAsDemoAsync(page);

        await Expect(page).ToHaveURLAsync(new Regex("/$"));
        await Expect(page.Locator("[data-testid='signed-in-as']")).ToHaveTextAsync(DemoDataSeeder.DemoEmail);

        await RelioAppFixture.ClosePageAsync(page);
    }
}
