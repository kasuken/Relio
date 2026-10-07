using System.Net;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class MarketingPricingTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData("None", false)]
    [InlineData("Stripe", false)]
    [InlineData("Stripe", true)]
    public async Task Unimplemented_billing_never_turns_prospective_configuration_into_an_offer(
        string provider, bool prospectiveOffers)
    {
        var environment = new Dictionary<string, string?> { ["Billing__Provider"] = provider };
        if (prospectiveOffers)
        {
            environment["Billing__Plans__0__Name"] = "UnapprovedSyntheticPlan";
            environment["Billing__Plans__0__Price"] = "9999.99";
            environment["Billing__Plans__0__Currency"] = "USD";
            environment["Billing__Plans__0__Approved"] = "true";
        }
        await using var app = VariantApp.Create(fixture, environment);
        var page = await app.NewPageAsync();
        var externalRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && uri.Authority != new Uri(app.Factory.ServerAddress).Authority)
            {
                externalRequests.Add(request.Url);
            }
        };
        var response = await page.GotoAsync("/pricing");
        response!.Status.Should().Be((int)HttpStatusCode.OK);
        var html = await response.TextAsync();
        html.Should().Contain("No commercial offers are published").And.Contain("not implemented yet")
            .And.NotContain("UnapprovedSyntheticPlan").And.NotContain("9999.99")
            .And.NotContain("stripe.com").And.NotContain("\"offers\"");
        await Expect(page.Locator("main h1")).ToHaveTextAsync("Hosting choices");
        await Expect(page.Locator("#self-hosting")).ToContainTextAsync("SQL Server");
        await Expect(page.Locator("main a[href='/Account/Register']")).ToHaveAttributeAsync("data-enhance-nav", "false");
        externalRequests.Should().BeEmpty();
    }
}
