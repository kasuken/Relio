using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public sealed class PrivacyLoggingTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Account_form_values_and_reset_tokens_do_not_appear_in_application_logs()
    {
        const string email = "private-email-sentinel@example.com";
        const string password = "Private-Password-Sentinel-123!";
        const string userId = "private-user-id-sentinel";
        const string resetToken = "private-reset-token-sentinel";
        const string newPassword = "Private-New-Password-Sentinel-123!";
        const string registrationEmail = "private-registration-email-sentinel@example.com";
        const string registrationPassword = "Private-Registration-Password-Sentinel-123!";

        var capture = new CapturingLoggerProvider();
        var factory = new RelioWebAppFactory(services =>
            services.AddSingleton<ILoggerProvider>(capture));
        _ = factory.Services;
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = factory.ServerAddress,
        });

        try
        {
            var page = await context.NewPageAsync();
            capture.Clear();

            await page.GotoAsync("/Account/Login");
            await page.GetByTestId("login-email").FillAsync(email);
            await page.GetByTestId("login-password").FillAsync(password);
            await page.GetByTestId("login-submit").ClickAsync();
            await Expect(page.GetByTestId("login-error"))
                .ToHaveTextAsync("Email or password is incorrect.");

            await page.GotoAsync(
                $"/Account/ResetPassword?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(resetToken)}");
            await page.GetByTestId("reset-password-new").FillAsync(newPassword);
            await page.GetByTestId("reset-password-confirm").FillAsync(newPassword);
            await page.GetByTestId("reset-password-submit").ClickAsync();
            await Expect(page.GetByTestId("reset-password-invalid")).ToBeVisibleAsync();

            await AccountTestHelpers.RegisterAsync(page, registrationEmail, registrationPassword);

            var logs = string.Join(Environment.NewLine, capture.Entries);
            logs.Should().NotContain(email);
            logs.Should().NotContain(password);
            logs.Should().NotContain(userId);
            logs.Should().NotContain(resetToken);
            logs.Should().NotContain(newPassword);
            logs.Should().NotContain(registrationEmail);
            logs.Should().NotContain(registrationPassword);
        }
        finally
        {
            await context.CloseAsync();
            await factory.DisposeAsync();
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyList<string> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

        public void Clear()
        {
            while (_entries.TryDequeue(out _))
            {
            }
        }

        public void Dispose()
        {
        }

        private void Add(string categoryName, LogLevel level, string message, Exception? exception)
        {
            var details = exception is null ? string.Empty : $" {exception}";
            _entries.Enqueue($"{categoryName} {level}: {message}{details}");
        }

        private sealed class CapturingLogger(string categoryName, CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Add(categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}
