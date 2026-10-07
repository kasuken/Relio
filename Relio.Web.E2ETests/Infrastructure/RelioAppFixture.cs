using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using Relio.Data.Seeding;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// Shared, once-per-test-run fixture: a real Relio.Web app (<see cref="RelioWebAppFactory"/>)
/// listening on a real Kestrel port with <c>Database:Provider=InMemory</c>, plus one shared,
/// headless Chromium instance. Tests ask it for a fresh <see cref="IPage"/> (own browser context,
/// so cookies/localStorage/the theme preference never leak between tests) rather than sharing
/// pages.
/// </summary>
/// <remarks>
/// Use via <see cref="RelioAppCollection"/> (<c>[Collection(RelioAppCollection.Name)]</c>) so
/// every test class in the collection shares this one app instance and browser - starting Kestrel
/// and Chromium per test class would be needlessly slow.
///
/// To exercise a variant of the app (e.g. <see cref="EmailConfirmationTests"/>'s test email sink),
/// construct a separate <c>new RelioWebAppFactory(configureTestServices: ...)</c> - see that
/// class's remarks for why, and <see cref="EmailConfirmationTests"/> for a worked example. It
/// starts its own independent real Kestrel host, so it never disturbs <see cref="App"/>.
/// </remarks>
public sealed class RelioAppFixture : IAsyncLifetime
{
    /// <summary>The running Relio.Web app. See the remarks above for creating variants of it.</summary>
    public RelioWebAppFactory App { get; private set; } = null!;

    /// <summary>The shared Playwright driver.</summary>
    public IPlaywright Playwright { get; private set; } = null!;

    /// <summary>The shared headless Chromium instance new pages/contexts are created from.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>The base URL (e.g. <c>http://127.0.0.1:53412</c>) the app is listening on.</summary>
    public string BaseUrl => App.ServerAddress;

    /// <summary>
    /// Where <see cref="ClosePageAsync"/> exports Playwright traces (one zip per test, always -
    /// not just on failure, since there is no cheap way to know a test's outcome from inside it
    /// with xUnit v2). CI only uploads this directory as a build artifact when the job fails (see
    /// .github/workflows/ci.yml); on a green run the zips are simply discarded with the rest of
    /// the build output.
    /// </summary>
    public static string TraceDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "playwright-traces");

    public async Task InitializeAsync()
    {
        App = new RelioWebAppFactory();

        // Forces WebApplicationFactory to actually build and Start the host (see
        // RelioWebAppFactory.CreateHost) so ServerAddress is populated before any test runs.
        _ = App.Services;

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        // This machine's cached Chromium build can be a different revision than the one this
        // Microsoft.Playwright version expects (see AGENTS.md "End-to-end tests"); set
        // RELIO_E2E_CHROMIUM to a system Chromium/Chrome binary to use that instead of the
        // bundled build. Left unset (the default, and what CI uses once it has run
        // `playwright.ps1 install chromium`), Playwright launches its own bundled browser.
        var executablePath = Environment.GetEnvironmentVariable("RELIO_E2E_CHROMIUM");

        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath,
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        Playwright?.Dispose();

        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }

    /// <summary>
    /// A fresh, isolated browser context (own cookies/localStorage) and page, navigated to
    /// nothing yet. Always use a fresh context per test: it is what keeps the dark-mode
    /// persistence and similar tests independent of each other.
    /// </summary>
    /// <param name="viewport">
    /// The viewport to open the page at (see <see cref="Viewports"/>). Defaults to
    /// <see cref="Viewports.Desktop"/>.
    /// </param>
    /// <param name="timezoneId">
    /// The browser context's IANA time zone (e.g. <c>"Pacific/Kiritimati"</c>), as
    /// <c>Intl.DateTimeFormat().resolvedOptions().timeZone</c> (and so <c>wwwroot/js/timezone.js</c>)
    /// reports it. Defaults to Playwright's own default (the host machine's time zone) when
    /// <see langword="null"/>.
    /// </param>
    /// <param name="locale">
    /// The browser context's BCP 47 locale, such as <c>"fr-FR"</c>, or null to use Playwright's
    /// default. Useful for proving that user input does not depend on the browser's locale.
    /// </param>
    public async Task<IPage> NewPageAsync(
        ViewportSize? viewport = null,
        string? timezoneId = null,
        string? locale = null)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = viewport ?? Viewports.Desktop,
            TimezoneId = timezoneId,
            Locale = locale,
        });

        // Always on (cheap for this suite's size): exported by ClosePageAsync below, so a failure
        // has a trace/screenshots to inspect without having to reproduce it locally.
        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });

        return await context.NewPageAsync();
    }

    /// <summary>
    /// Navigates to <paramref name="path"/> (relative to <see cref="BaseUrl"/>) and waits for
    /// MainLayout's first interactive render to complete (see wwwroot/js/theme.js and
    /// MainLayout.razor's <c>data-app-ready</c> marker), so callers never race Blazor Server's
    /// circuit connecting.
    /// </summary>
    public static async Task GotoAndWaitForInteractiveAsync(IPage page, string path = "/dashboard")
    {
        await page.GotoAsync(path);
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
    }

    /// <summary>
    /// Signs <paramref name="page"/> in as the seeded demo account (see
    /// <see cref="DemoDataSeeder"/>; enabled for every test by <see cref="RelioWebAppFactory"/>)
    /// through the real login page, and waits for the resulting redirect's interactive render to
    /// finish. Every protected page requires authentication (see AGENTS.md "Protect app pages"),
    /// so call this before navigating to anything other than <c>/Account/*</c> or <c>/health/*</c>.
    /// </summary>
    public static async Task SignInAsDemoAsync(IPage page)
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("[data-testid='login-email']").FillAsync(DemoDataSeeder.DemoEmail);
        await page.Locator("[data-testid='login-password']").FillAsync(DemoDataSeeder.DemoPassword);
        await page.Locator("[data-testid='login-submit']").ClickAsync();
        await page.Locator("html[data-app-ready='true']").WaitForAsync();
    }

    /// <summary>
    /// Ends a test: exports its Playwright trace (see <see cref="TraceDirectory"/>) and closes its
    /// browser context. Always call this instead of <c>page.Context.CloseAsync()</c> directly, so
    /// every test leaves a trace behind.
    /// </summary>
    /// <param name="page">The page returned by <see cref="NewPageAsync"/>.</param>
    /// <param name="testName">
    /// Names the exported trace file; defaults to the calling test method's name.
    /// </param>
    public static async Task ClosePageAsync(IPage page, [CallerMemberName] string testName = "")
    {
        Directory.CreateDirectory(TraceDirectory);
        var tracePath = Path.Combine(TraceDirectory, $"{testName}-{Guid.NewGuid():N}.zip");

        await page.Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
        await page.Context.CloseAsync();
    }
}
