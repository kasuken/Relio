using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Relio.Web.Tests.Infrastructure;

/// <summary>Records everything a logger factory delivers, including structured state, for leak assertions.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    /// <summary>The entries captured so far, oldest first.</summary>
    public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToArray();

    /// <summary>Every captured category, event, message, structured state and exception as one string.</summary>
    public string AllText => string.Join(
        '\n',
        Entries.Select(entry =>
            $"{entry.Category} {entry.EventId} {entry.Message} {entry.StateText} {entry.Exception}"));

    /// <summary>Forgets everything captured so far.</summary>
    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
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
            // StateText is what a JSON console formatter would print under "State".
            var stateText = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(';', pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : string.Empty;
            owner._entries.Enqueue(new CapturedLogEntry(
                category,
                logLevel,
                eventId,
                formatter(state, exception),
                stateText,
                exception));
        }
    }
}

/// <summary>One captured log call.</summary>
public sealed record CapturedLogEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    string StateText,
    Exception? Exception);
