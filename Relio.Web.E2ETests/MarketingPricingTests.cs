using System.Net;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

/// <summary>
/// The pricing page and the plan page with and without hosted billing. Stripe is configured with
/// obviously fake values: nothing here ever reaches Stripe (no checkout is started), and the pages
/// load no third-party resource.
/// </summary>
[Collection(RelioAppCollection.Name)]
public sealed class MarketingPricingTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_billing_the_pricing_page_publishes_no_offer_even_with_prospective_plan_settings(bool prospectiveOffers)
    {
        var environment = new Dictionary<string, string?> { ["Billing__Provider"] = "None" };
        if (prospectiveOffers)
        {
            environment["Billing__Plans__0__Name"] = "UnapprovedSyntheticPlan";
            environment["Billing__Plans__0__Price"] = "9999.99";
        }

        await using var app = VariantApp.Create(fixture, environment);
        var page = await app.NewPageAsync();
        var externalRequests = TrackExternalRequests(page, app);

        var response = await page.GotoAsync("/pricing");

        response!.Status.Should().Be((int)HttpStatusCode.OK);
        var html = await response.TextAsync();
        html.Should().Contain("This instance has no paid plans")
            .And.NotContain("UnapprovedSyntheticPlan").And.NotContain("9999.99")
            .And.NotContain("$2 / month").And.NotContain("stripe.com").And.NotContain("\"offers\"");
        await Expect(page.Locator("main h1")).ToHaveTextAsync("Hosting choices");
        await Expect(page.Locator("#self-hosting")).ToContainTextAsync("SQL Server");
        await Expect(page.Locator("main a[href='/Account/Register']")).ToHaveAttributeAsync("data-enhance-nav", "false");
        externalRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task With_stripe_the_pricing_page_shows_the_free_plan_and_relio_pro_prices()
    {
        await using var app = VariantApp.Create(fixture, StripeEnvironment());
        var page = await app.NewPageAsync();
        var externalRequests = TrackExternalRequests(page, app);

        var response = await page.GotoAsync("/pricing");

        response!.Status.Should().Be((int)HttpStatusCode.OK);
        await Expect(page.Locator("main h1")).ToHaveTextAsync("Plans and pricing");
        await Expect(page.Locator("[data-testid='pricing-plan-free']")).ToContainTextAsync("Up to 25 active people");
        var pro = page.Locator("[data-testid='pricing-plan-pro']");
        await Expect(pro).ToContainTextAsync("$1 / month");
        await Expect(pro).ToContainTextAsync("$12 / year");
        await Expect(pro).ToContainTextAsync("$2 / month");
        await Expect(page.Locator("[data-testid='pricing-subscribe']")).ToHaveAttributeAsync("href", "/Account/Manage/Plan");
        await Expect(page.Locator("#self-hosting")).ToContainTextAsync("no plans and no limits");
        (await response.TextAsync()).Should().NotContain("stripe.com").And.NotContain("\"offers\"");
        externalRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task With_stripe_a_new_account_sees_the_free_plan_and_can_choose_monthly_or_yearly()
    {
        await using var app = VariantApp.Create(fixture, StripeEnvironment());
        var page = await app.NewPageAsync();
        var externalRequests = TrackExternalRequests(page, app);
        await AccountTestHelpers.RegisterAsync(page, AccountTestHelpers.NewEmail("plan"), AccountTestHelpers.StrongPassword);

        // The pricing page's call to action sends a signed-in user straight to the plan page.
        await page.GotoAsync("/Account/Manage/Plan");

        await Expect(page.Locator("[data-testid='plan-current']")).ToHaveTextAsync("Free");
        await Expect(page.Locator("[data-testid='plan-usage']")).ToHaveTextAsync("0 of 25 active people");
        await Expect(page.Locator("[data-testid='plan-subscribe-yearly']")).ToContainTextAsync("$12 / year ($1 / month)");
        await Expect(page.Locator("[data-testid='plan-subscribe-monthly']")).ToContainTextAsync("$2 / month");
        await Expect(page.Locator("[data-testid='plan-manage-billing']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='plan-stripe-notice']")).ToBeVisibleAsync();

        // A cancelled checkout comes back here with a calm note.
        await page.GotoAsync("/Account/Manage/Plan?checkout=cancelled");
        await Expect(page.Locator("[data-testid='plan-checkout-notice']")).ToContainTextAsync("You have not been charged");

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");
        await Expect(page.Locator("[data-testid='settings-plan-link']")).ToHaveAttributeAsync("href", "/Account/Manage/Plan");
        externalRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task With_stripe_the_csp_allows_only_stripes_hosted_pages_as_form_targets()
    {
        await using var app = VariantApp.Create(fixture, StripeEnvironment());
        var page = await app.NewPageAsync();

        var response = await page.GotoAsync("/pricing");

        var policy = response!.Headers["content-security-policy"];
        policy.Should().Contain("form-action 'self' https://checkout.stripe.com https://billing.stripe.com;")
            .And.Contain("connect-src 'self'").And.Contain("script-src 'self' 'nonce-");
    }

    [Fact]
    public async Task Without_billing_the_plan_page_says_everything_is_included_and_settings_has_no_plan_link()
    {
        var page = await fixture.NewPageAsync(Viewports.Desktop);
        try
        {
            await RelioAppFixture.SignInAsDemoAsync(page);

            await page.GotoAsync("/Account/Manage/Plan");
            await Expect(page.Locator("[data-testid='plan-not-available']")).ToContainTextAsync("no paid plans");
            await Expect(page.Locator("[data-testid='plan-subscribe-yearly']")).ToHaveCountAsync(0);

            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/settings");
            await Expect(page.Locator("[data-testid='settings-plan-link']")).ToHaveCountAsync(0);
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    private static Dictionary<string, string?> StripeEnvironment() => new()
    {
        ["Billing__Provider"] = "Stripe",
        ["Billing__ApiKey"] = "sk_test_fake_key_never_used",
        ["Billing__WebhookSigningSecret"] = "whsec_fake_secret_never_used",
        ["Billing__ProMonthlyPriceId"] = "price_fake_monthly",
        ["Billing__ProYearlyPriceId"] = "price_fake_yearly",
        ["Billing__CheckoutSuccessUrl"] = "https://relio.example.test/Account/Manage/Plan",
        ["Billing__CheckoutCancelUrl"] = "https://relio.example.test/Account/Manage/Plan",
        ["Billing__PortalReturnUrl"] = "https://relio.example.test/Account/Manage/Plan",
    };

    private static List<string> TrackExternalRequests(IPage page, VariantApp app)
    {
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
        return externalRequests;
    }
}
