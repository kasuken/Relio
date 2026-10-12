using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Relio.Web.Diagnostics;
using Relio.Web.Security;
using Relio.Web.Tests.Infrastructure;

namespace Relio.Web.Tests.Diagnostics;

public sealed class CircuitFailureSignalLoggerProviderTests
{
    private const string NoteSentinel = "private-note-text-sentinel-123";
    private const string DifficultMomentSentinel = "private-difficult-moment-sentinel-456";
    private const string CircuitIdSentinel = "circuit-id-sentinel-789";
    private const string CircuitHostCategory = "Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost";

    [Fact]
    public void Unhandled_circuit_exception_emits_sanitized_signal_without_private_values()
    {
        var capture = new CapturingLoggerProvider();
        using var factory = CreateFactory(capture);

        factory.CreateLogger(CircuitHostCategory).Log(
            LogLevel.Error,
            new EventId(111, "CircuitUnhandledException"),
            new InvalidOperationException($"Note: {NoteSentinel}; moment: {DifficultMomentSentinel}"),
            "Unhandled exception in circuit '{CircuitId}'.",
            CircuitIdSentinel);

        var entry = capture.Entries.Should().ContainSingle().Subject;
        entry.Category.Should().Be(RelioOperationalEvents.CircuitFailureCategory);
        entry.EventId.Id.Should().Be(RelioOperationalEvents.CircuitFailureId);
        entry.EventId.Name.Should().Be("RelioCircuitFailure");
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain(nameof(InvalidOperationException));
        entry.Message.Should().Contain("CircuitHost.CircuitUnhandledException");
        entry.Message.Should().Contain(
            $"circuit {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("relio-circuit:" + CircuitIdSentinel)))[..12].ToLowerInvariant()})");
        entry.Exception.Should().BeNull();
        capture.AllText.Should().NotContain(NoteSentinel)
            .And.NotContain(DifficultMomentSentinel)
            .And.NotContain(CircuitIdSentinel);
    }

    [Fact]
    public void Renderer_warning_with_exception_message_property_is_not_translated_or_leaked()
    {
        var capture = new CapturingLoggerProvider();
        using var factory = CreateFactory(capture);

        factory.CreateLogger("Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer").Log(
            LogLevel.Warning,
            new EventId(100, "ExceptionRenderingComponent"),
            new InvalidOperationException(NoteSentinel),
            "Unhandled exception rendering component: {Message}",
            NoteSentinel);

        capture.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Circuit_handler_failure_template_with_message_is_sanitized()
    {
        var capture = new CapturingLoggerProvider();
        using var factory = CreateFactory(capture);

        factory.CreateLogger(CircuitHostCategory).Log(
            LogLevel.Error,
            new EventId(110, "CircuitHandlerFailed"),
            new InvalidOperationException(DifficultMomentSentinel),
            "Unhandled error invoking circuit handler type {handlerType}.{handlerMethod}: {Message}",
            "X",
            "Y",
            DifficultMomentSentinel);

        var entry = capture.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(RelioOperationalEvents.CircuitFailureId);
        entry.Message.Should().Contain("circuit none");
        entry.Message.Should().Contain("CircuitHost.CircuitHandlerFailed");
        capture.AllText.Should().NotContain(DifficultMomentSentinel);
    }

    [Fact]
    public void Non_circuit_categories_are_not_translated()
    {
        var capture = new CapturingLoggerProvider();
        using var factory = CreateFactory(capture);

        factory.CreateLogger("Microsoft.EntityFrameworkCore.Update").LogError(
            new InvalidOperationException(NoteSentinel),
            "Update failed.");
        factory.CreateLogger("Relio.Web.Anything").LogError(
            new InvalidOperationException(NoteSentinel),
            "Anything failed.");

        capture.Entries.Where(entry => entry.EventId.Id == RelioOperationalEvents.CircuitFailureId)
            .Should().BeEmpty();
    }

    [Fact]
    public void Framework_events_never_reach_other_providers()
    {
        var capture = new CapturingLoggerProvider();
        using var factory = CreateFactory(capture);

        var logger = factory.CreateLogger(CircuitHostCategory);
        logger.LogError(new InvalidOperationException(NoteSentinel), "Unhandled exception in circuit '{CircuitId}'.", CircuitIdSentinel);
        logger.LogWarning("Something odd about {Value}.", DifficultMomentSentinel);

        capture.Entries.Should().NotContain(entry =>
            entry.Category.StartsWith(CircuitFailureSignalLoggerProvider.WatchedCategoryPrefix, StringComparison.Ordinal));
        capture.AllText.Should().NotContain(NoteSentinel).And.NotContain(DifficultMomentSentinel);
    }

    [Fact]
    public void Failure_reported_after_the_container_is_disposed_does_not_throw()
    {
        var provider = new CircuitFailureSignalLoggerProvider(new DisposedServiceProvider());
        var logger = provider.CreateLogger(CircuitHostCategory);

        var act = () => logger.Log(
            LogLevel.Error,
            new EventId(111, "CircuitUnhandledException"),
            new InvalidOperationException(NoteSentinel),
            "Unhandled exception in circuit '{CircuitId}'.",
            CircuitIdSentinel);

        act.Should().NotThrow();
    }

    private static ILoggerFactory CreateFactory(CapturingLoggerProvider capture)
    {
        // Mirrors the hostile configuration of PrivacyLoggingBuilderExtensionsTests: everything enabled,
        // including a provider-specific section for the translator.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore.Components.Server"] = "Trace",
                ["Logging:RelioCircuitFailureSignal:LogLevel:Default"] = "None",
            })
            .Build();

        return LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(capture);
            logging.AddRelioPrivacyLoggingFilters();
            logging.AddRelioCircuitFailureSignal();
        });
    }

    private sealed class DisposedServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new ObjectDisposedException("container");
    }
}
