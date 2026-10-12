using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Relio.Application.Dashboard;
using Relio.Web.Diagnostics;
using Relio.Web.E2ETests.Infrastructure;
using Relio.Web.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

/// <summary>
/// Proves against a real Blazor circuit that an unhandled interactive failure becomes one sanitized
/// operational event (9002) while the framework's own, message-carrying log lines stay suppressed.
/// </summary>
[Collection(RelioAppCollection.Name)]
public sealed class CircuitFailureSignalTests(RelioAppFixture fixture)
{
    private const string NoteSentinel = "private-note-text-sentinel-123";
    private const string DifficultMomentSentinel = "private-difficult-moment-sentinel-456";

    [Fact]
    public async Task Unhandled_circuit_failure_is_observable_without_private_values()
    {
        var capture = new CapturingLoggerProvider();
        var failureSwitch = new FailureSwitch();
        var factory = new RelioWebAppFactory(services =>
        {
            services.AddSingleton<ILoggerProvider>(capture);
            services.AddSingleton(failureSwitch);

            // Decorate the dashboard service (loaded by the dashboard page's OnInitializedAsync).
            var original = services.Last(descriptor => descriptor.ServiceType == typeof(IDashboardService));
            services.Remove(original);
            services.AddScoped<IDashboardService>(provider => new SwitchableDashboardService(
                (IDashboardService)original.ImplementationFactory!(provider),
                failureSwitch));
        });
        _ = factory.Services;
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = factory.ServerAddress,
        });

        try
        {
            var page = await context.NewPageAsync();
            await RelioAppFixture.SignInAsDemoAsync(page);
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/people");
            capture.Clear();

            failureSwitch.Armed = true;
            // In-circuit navigation: the dashboard's interactive OnInitializedAsync throws.
            await page.Locator("nav[aria-label='Primary']").GetByText("Dashboard", new() { Exact = true }).ClickAsync();

            await Expect(page.Locator("#blazor-error-ui")).ToBeVisibleAsync();
            var signal = await WaitForSignalAsync(capture);

            signal.Message.Should().Contain(nameof(InvalidOperationException));
            signal.Message.Should().Contain("CircuitHost.CircuitUnhandledException");
            signal.Exception.Should().BeNull();
            capture.AllText.Should().NotContain(NoteSentinel).And.NotContain(DifficultMomentSentinel);
            capture.Entries.Should().NotContain(entry =>
                entry.Category.StartsWith("Microsoft.AspNetCore.Components.Server", StringComparison.Ordinal));
        }
        finally
        {
            await context.CloseAsync();
            await factory.DisposeAsync();
        }
    }

    private static async Task<CapturedLogEntry> WaitForSignalAsync(CapturingLoggerProvider capture)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var signal = capture.Entries.FirstOrDefault(
                entry => entry.EventId.Id == RelioOperationalEvents.CircuitFailureId);
            if (signal is not null)
            {
                return signal;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("No circuit failure signal (event 9002) was logged within 10 seconds.");
    }

    private sealed class FailureSwitch
    {
        public volatile bool Armed;
    }

    private sealed class SwitchableDashboardService(IDashboardService inner, FailureSwitch failureSwitch) : IDashboardService
    {
        public Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
            failureSwitch.Armed
                ? throw new InvalidOperationException($"{NoteSentinel} {DifficultMomentSentinel}")
                : inner.GetAsync(cancellationToken);
    }
}
