using System.Net;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class MarketingRoutingTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Public_root_is_static_and_private_routes_still_require_sign_in()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(fixture.BaseUrl),
        };
        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("A private notebook for your relationships")
            .And.MatchRegex("href=\"marketing(?:\\.[a-z0-9]+)?\\.css\"")
            .And.NotContain("\"type\":\"server\"")
            .And.NotContain("demo@relio.local");

        foreach (var path in new[] { "/dashboard", "/people", "/settings", "/admin/users", "/admin/metrics" })
        {
            using var privateResponse = await client.GetAsync(path);
            privateResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
            privateResponse.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
        }
    }

    [Theory]
    [InlineData("Open", true, true)]
    [InlineData("InviteOnly", true, false)]
    [InlineData("Closed", true, false)]
    [InlineData("InviteOnly", false, true)]
    [InlineData("Closed", false, true)]
    public async Task Public_registration_actions_follow_instance_eligibility(
        string mode, bool hasAccounts, bool canRegister)
    {
        await using var app = VariantApp.Create(
            fixture, new Dictionary<string, string?> { ["Registration__Mode"] = mode }, seedDemoData: hasAccounts);
        var page = await app.NewPageAsync();
        await page.GotoAsync("/");
        await Expect(page.Locator("header a[href='/Account/Login']")).ToBeVisibleAsync();
        await Expect(page.Locator("header a[href='/Account/Register']")).ToHaveCountAsync(canRegister ? 1 : 0);
        if (canRegister)
        {
            await Expect(page.Locator("header a[href='/Account/Register']")).ToHaveAttributeAsync("data-enhance-nav", "false");
        }
    }

    [Fact]
    public async Task Marketing_registration_captures_timezone_and_signed_in_root_opens_workspace()
    {
        var page = await fixture.NewPageAsync(Viewports.Phone, timezoneId: "Pacific/Kiritimati");
        try
        {
            await page.GotoAsync("/");
            await page.Locator("header a[href='/Account/Register']").ClickAsync();
            await Expect(page.Locator("#register-timezone-input")).ToHaveValueAsync("Pacific/Kiritimati");
            var email = AccountTestHelpers.NewEmail("marketing");
            await page.GetByTestId("register-email").FillAsync(email);
            await page.GetByTestId("register-password").FillAsync(AccountTestHelpers.StrongPassword);
            await page.GetByTestId("register-confirm-password").FillAsync(AccountTestHelpers.StrongPassword);
            await page.GetByTestId("register-submit").ClickAsync();
            await Expect(page.GetByTestId("onboarding-page")).ToBeVisibleAsync();

            await page.GotoAsync("/");
            await Expect(page.Locator("header a[href='/dashboard']")).ToHaveTextAsync("Open Relio");
            await Expect(page.Locator("header a[href='/Account/Register']")).ToHaveCountAsync(0);
            await page.Locator("header a[href='/dashboard']").ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            await Expect(page.GetByTestId("signed-in-as")).ToHaveTextAsync(email);
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }
}
