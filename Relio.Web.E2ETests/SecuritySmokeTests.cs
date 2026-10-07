using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class SecuritySmokeTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Registration_login_and_private_quick_log_work_without_csp_console_errors()
    {
        var email = NewEmail("security-smoke");
        var page = await fixture.NewPageAsync(timezoneId: "Pacific/Kiritimati");
        var cspErrors = new ConcurrentQueue<string>();
        var signalRSocketWithFrame = new TaskCompletionSource<IWebSocket>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        page.Console += (_, message) =>
        {
            if (message.Type == "error"
                && (message.Text.Contains("content security policy", StringComparison.OrdinalIgnoreCase)
                    || message.Text.Contains("violates the following", StringComparison.OrdinalIgnoreCase)))
            {
                cspErrors.Enqueue(message.Text);
            }
        };
        try
        {
            await RegisterAsync(page, email, StrongPassword);
            (await GetStoredTimeZoneAsync(fixture.App, email)).Should().Be("Pacific/Kiritimati");

            await SignOutAsync(page);
            await page.GotoAsync("/interactions/new");
            await Expect(page).ToHaveURLAsync(new Regex("/Account/Login(?:\\?|$)"));

            await LoginAndWaitForAppAsync(page, email, StrongPassword);
            page.WebSocket += (_, webSocket) =>
            {
                if (Uri.TryCreate(webSocket.Url, UriKind.Absolute, out var socketUri)
                    && string.Equals(socketUri.AbsolutePath, "/_blazor", StringComparison.OrdinalIgnoreCase))
                {
                    webSocket.FrameReceived += (_, _) => signalRSocketWithFrame.TrySetResult(webSocket);
                }
            };
            await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, "/dashboard");
            var signalRSocket = await signalRSocketWithFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
            signalRSocket.IsClosed.Should().BeFalse();
            var appUri = new Uri(fixture.BaseUrl);
            var socketUri = new Uri(signalRSocket.Url);
            socketUri.Host.Should().Be(appUri.Host);
            socketUri.Port.Should().Be(appUri.Port);
            socketUri.Scheme.Should().Be(appUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws");

            var dashboardQuickLog = page.GetByTestId("dashboard-log-interaction");
            await Expect(dashboardQuickLog).ToBeVisibleAsync();
            await dashboardQuickLog.ClickAsync();
            await Expect(page.GetByTestId("quick-log-no-people")).ToBeVisibleAsync();

            await page.GotoAsync("/Account/ForgotPassword");
            await page.GetByTestId("forgot-password-email").FillAsync(email);
            await page.GetByTestId("forgot-password-submit").ClickAsync();
            await Expect(page.GetByTestId("forgot-password-confirmation-heading")).ToBeVisibleAsync();

            cspErrors.Should().BeEmpty();
        }
        finally
        {
            await RelioAppFixture.ClosePageAsync(page);
        }
    }

    private static async Task<string?> GetStoredTimeZoneAsync(RelioWebAppFactory app, string email)
    {
        var user = await GetUserAsync(app, email);
        user.Should().NotBeNull();

        using var scope = app.CreateRealScope();
        return await scope.ServiceProvider.GetRequiredService<RelioDbContext>().UserProfiles
            .AsNoTracking()
            .Where(profile => profile.OwnerId == user!.Id)
            .Select(profile => profile.TimeZoneId)
            .SingleAsync();
    }
}
