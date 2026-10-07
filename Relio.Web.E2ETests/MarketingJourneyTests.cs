using System.Collections.Concurrent;
using Microsoft.Playwright;
using Relio.Data.Seeding;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class MarketingJourneyTests(RelioAppFixture fixture)
{
    [Theory]
    [InlineData(360, "light")]
    [InlineData(360, "dark")]
    [InlineData(768, "light")]
    [InlineData(1440, "dark")]
    public async Task Public_pages_are_keyboard_usable_and_local_at_text_zoom_and_reduced_motion(
        int width, string theme)
    {
        var page = await fixture.NewPageAsync(new ViewportSize { Width = width, Height = 900 });
        var remoteRequests = new ConcurrentBag<string>();
        var sockets = new ConcurrentBag<string>();
        page.Request += (_, request) =>
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && uri.Authority != new Uri(fixture.BaseUrl).Authority)
            {
                remoteRequests.Add(request.Url);
            }
        };
        page.WebSocket += (_, socket) => sockets.Add(socket.Url);
        try
        {
            await page.AddInitScriptAsync($"localStorage.setItem('relio-theme', '{theme}');");
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
            foreach (var route in new[] { "/", "/features", "/pricing", "/changelog" })
            {
                await page.GotoAsync(route);
                await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", theme);
                await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
                await Expect(page.Locator("main")).ToHaveCountAsync(1);
                await Expect(page.Locator("h1")).ToHaveCountAsync(1);
                await Expect(page.Locator("header nav[aria-label='Public navigation'] a")).ToHaveCountAsync(3);
                (await page.EvaluateAsync<bool>(
                    "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"))
                    .Should().BeTrue($"marketing route {route} fits a {width}px viewport at 200% text size");
                (await page.EvaluateAsync<double[]>(
                    "() => document.querySelector('.rl-marketing-shell').getAnimations({subtree: true}).map(animation => animation.effect.getComputedTiming().activeDuration)"))
                    .Should().OnlyContain(duration => duration <= 0.01, "reduced-motion transitions use the global 0.01ms limit");
                var contrasts = await page.EvaluateAsync<double[]>("""
                    () => {
                        const css = getComputedStyle(document.documentElement);
                        const luminance = token => {
                            const hex = css.getPropertyValue(token).trim().slice(1);
                            const rgb = [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
                                .map(v => v <= .04045 ? v / 12.92 : Math.pow((v + .055) / 1.055, 2.4));
                            return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722;
                        };
                        const ratio = (a, b) => {
                            const x = luminance(a), y = luminance(b);
                            return (Math.max(x, y) + .05) / (Math.min(x, y) + .05);
                        };
                        return [ratio('--text', '--paper'), ratio('--text-muted', '--paper'),
                            ratio('--pen', '--paper'), ratio('--on-pen', '--pen')];
                    }
                    """);
                contrasts.Should().OnlyContain(ratio => ratio >= 4.5);
                var skip = page.GetByRole(AriaRole.Link, new() { Name = "Skip to content", Exact = true });
                await TabToAsync(page, skip, "Shift+Tab");
                await Expect(skip).ToBeInViewportAsync();
                (await skip.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"))
                    .Should().Be("solid");
                await page.Keyboard.PressAsync("Enter");
                await Expect(page.Locator("#marketing-main")).ToBeFocusedAsync();
                await TabToAsync(page, page.Locator("header a[href='/features']"));
                await page.Keyboard.PressAsync("Enter");
                await Expect(page.Locator("main h1")).ToHaveTextAsync("Keep the details that help you show up");
                await Expect(page.Locator("head link[href*='marketing'][rel='stylesheet']")).ToHaveCountAsync(1);
                await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
            }
            remoteRequests.Should().BeEmpty();
            sockets.Should().BeEmpty("public marketing pages must not start a circuit");
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Enhanced_public_navigation_keeps_metadata_then_account_and_workspace_boundaries_work()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoAsync("/");
            foreach (var route in new[] { "/features", "/pricing", "/changelog" })
            {
                await page.Locator($"header a[href='{route}']").ClickAsync();
                await Expect(page.Locator("link[rel='canonical']")).ToHaveAttributeAsync("href", "https://localhost" + route);
                await Expect(page.Locator("title")).ToHaveCountAsync(1);
                await Expect(page.Locator("meta[name='description']")).ToHaveCountAsync(1);
                await Expect(page.Locator("head link[href*='marketing'][rel='stylesheet']")).ToHaveCountAsync(1);
            }
            await page.Locator("header a[href='/Account/Login']").ClickAsync();
            await page.GetByTestId("login-email").FillAsync(DemoDataSeeder.DemoEmail);
            await page.GetByTestId("login-password").FillAsync(DemoDataSeeder.DemoPassword);
            await page.GetByTestId("login-submit").ClickAsync();
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            new Uri(page.Url).AbsolutePath.Should().Be("/dashboard");
            var publicResponse = await page.GotoAsync("/");
            (await publicResponse!.AllHeadersAsync())["cache-control"].Should().Contain("no-store");
            await Expect(page.Locator("header a[href='/dashboard']")).ToHaveTextAsync("Open Relio");
            await Expect(page.Locator("main")).Not.ToContainTextAsync(DemoDataSeeder.DemoEmail);
            await page.Locator("header a[href='/dashboard']").ClickAsync();
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            await page.GetByTestId("sign-out").ClickAsync();
            await Expect(page.GetByTestId("login-submit")).ToBeVisibleAsync();
            await page.GoBackAsync();
            await Expect(page.GetByTestId("signed-in-as")).Not.ToBeVisibleAsync();
            await page.GotoAsync("/dashboard");
            new Uri(page.Url).AbsolutePath.Should().Be("/Account/Login");
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    [Fact]
    public async Task Private_deep_link_is_preserved_through_sign_in()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoAsync("/settings");
            await page.GetByTestId("login-email").FillAsync(DemoDataSeeder.DemoEmail);
            await page.GetByTestId("login-password").FillAsync(DemoDataSeeder.DemoPassword);
            await page.GetByTestId("login-submit").ClickAsync();
            await page.Locator("html[data-app-ready='true']").WaitForAsync();
            new Uri(page.Url).AbsolutePath.Should().Be("/settings");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Settings", Exact = true })).ToBeVisibleAsync();
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    private static async Task TabToAsync(IPage page, ILocator target, string key = "Tab")
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await target.EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }
            await page.Keyboard.PressAsync(key);
        }
        await Expect(target).ToBeFocusedAsync();
    }
}
