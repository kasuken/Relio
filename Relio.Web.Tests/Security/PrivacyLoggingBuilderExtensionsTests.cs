using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Relio.Web.Security;

namespace Relio.Web.Tests.Security;

public sealed class PrivacyLoggingBuilderExtensionsTests
{
    [Fact]
    public void Sensitive_framework_logs_stay_disabled_when_configuration_tries_to_enable_them()
    {
        const string privateToken = "private-network-token-sentinel";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore.SignalR"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel"] = "Trace",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Trace",
            })
            .Build();
        var provider = new CapturingLoggerProvider();

        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
            logging.Services.Configure<LoggerFilterOptions>(options =>
                options.Rules.Add(new LoggerFilterRule(
                    typeof(CapturingLoggerProvider).FullName,
                    "Microsoft.AspNetCore.SignalR.HubConnectionHandler",
                    LogLevel.Trace,
                    filter: null)));
            logging.AddRelioPrivacyLoggingFilters();
        });

        foreach (var category in new[]
                 {
                     "Microsoft.AspNetCore.Hosting.Diagnostics",
                     "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware",
                     "Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware",
                     "Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost",
                     "Microsoft.AspNetCore.SignalR.HubConnectionHandler",
                     "Microsoft.AspNetCore.Http.Connections.Internal.HttpConnectionDispatcher",
                     "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware",
                     "Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServer",
                     "Microsoft.EntityFrameworkCore.Query.QueryCompilationContext",
                     "Microsoft.EntityFrameworkCore.Database.Command",
                     "Microsoft.EntityFrameworkCore.Database.Connection",
                     "Microsoft.EntityFrameworkCore.Update",
                 })
        {
            var logger = loggerFactory.CreateLogger(category);
            var exception = new InvalidOperationException(privateToken);
            logger.LogTrace("Request carried {PrivateToken}.", privateToken);
            logger.LogError(exception, "Operation failed for {PrivateToken}.", privateToken);
        }

        provider.Entries.Should().BeEmpty();

        loggerFactory.CreateLogger("Relio.Web.Security.HttpFailureBoundary")
            .LogError(
                "HTTP request failed with exception type {ExceptionType}.",
                nameof(InvalidOperationException));

        var safeDiagnostic = provider.Entries.Should().ContainSingle().Subject;
        safeDiagnostic.Message.Should().Contain(nameof(InvalidOperationException));
        safeDiagnostic.Message.Should().NotContain(privateToken);
        safeDiagnostic.Exception.Should().BeNull();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedEntry> _entries = new();

        public IReadOnlyList<CapturedEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            string categoryName,
            CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                owner._entries.Enqueue(new CapturedEntry(
                    $"{categoryName} {logLevel}: {formatter(state, exception)}",
                    exception));
            }
        }
    }

    private sealed record CapturedEntry(string Message, Exception? Exception);
}
