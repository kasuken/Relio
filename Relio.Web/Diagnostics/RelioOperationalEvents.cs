using Microsoft.Extensions.Logging;

namespace Relio.Web.Diagnostics;

/// <summary>
/// The stable catalogue of operational failure events an operator can alert on.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 9001-9099 are reserved for Relio operational events and are a public contract:
/// <c>docs/operations/monitoring.md</c> and <c>infra/modules/monitoring.bicep</c> match on them.
/// Never renumber or reuse an id.
/// </para>
/// <para>
/// Parameters are limited to constant-like strings (exception type names, framework event names,
/// hashes). Never pass an <see cref="Exception"/>, a user id, an email address, a route or any entity
/// value, and never log an exception message or stack trace: framework and database messages can
/// quote private note, difficult-moment or form content.
/// </para>
/// </remarks>
public static partial class RelioOperationalEvents
{
    /// <summary>Event id of <see cref="UnhandledHttpFailure"/>.</summary>
    public const int UnhandledHttpFailureId = 9001;

    /// <summary>Event id of <see cref="CircuitFailure"/>.</summary>
    public const int CircuitFailureId = 9002;

    /// <summary>Event id of <see cref="ReminderJobFailure"/>.</summary>
    public const int ReminderJobFailureId = 9003;

    /// <summary>Event id of <see cref="ProductMetricsRetentionFailure"/>.</summary>
    public const int ProductMetricsRetentionFailureId = 9004;

    /// <summary>Event id of <see cref="BackupFailure"/>.</summary>
    public const int BackupFailureId = 9005;

    /// <summary>Logger category used by the sanitized circuit-failure signal.</summary>
    public const string CircuitFailureCategory = "Relio.Web.Diagnostics.CircuitFailure";

    /// <summary>Returns the exception type name used in every operational signal, or "None".</summary>
    /// <param name="exception">The failure, when there is one.</param>
    public static string ExceptionTypeOf(Exception? exception) => exception?.GetType().Name ?? "None";

    /// <summary>An unhandled exception escaped an HTTP request.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type name.</param>
    [LoggerMessage(
        EventId = UnhandledHttpFailureId,
        EventName = "RelioUnhandledHttpFailure",
        Level = LogLevel.Error,
        Message = "HTTP request failed with exception type {ExceptionType}.")]
    public static partial void UnhandledHttpFailure(ILogger logger, string exceptionType);

    /// <summary>An interactive (Blazor Server) circuit failed with an unhandled exception.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type name.</param>
    /// <param name="frameworkEvent">The framework category segment and event name that reported it.</param>
    /// <param name="circuitRef">A short one-way hash of the circuit id, for correlation only.</param>
    [LoggerMessage(
        EventId = CircuitFailureId,
        EventName = "RelioCircuitFailure",
        Level = LogLevel.Error,
        Message = "Interactive circuit failed with exception type {ExceptionType} (framework event {FrameworkEvent}, circuit {CircuitRef}).")]
    public static partial void CircuitFailure(ILogger logger, string exceptionType, string frameworkEvent, string circuitRef);

    /// <summary>A reminder scheduler run failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type name.</param>
    [LoggerMessage(
        EventId = ReminderJobFailureId,
        EventName = "RelioReminderJobFailure",
        Level = LogLevel.Error,
        Message = "Reminder scheduler job failed with exception type {ExceptionType}.")]
    public static partial void ReminderJobFailure(ILogger logger, string exceptionType);

    /// <summary>A product metrics retention run failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type name.</param>
    [LoggerMessage(
        EventId = ProductMetricsRetentionFailureId,
        EventName = "RelioProductMetricsRetentionFailure",
        Level = LogLevel.Error,
        Message = "Product metrics retention failed with exception type {ExceptionType}.")]
    public static partial void ProductMetricsRetentionFailure(ILogger logger, string exceptionType);

    /// <summary>A backup job failed. Reserved for the backup automation (#119); nothing emits it yet.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type name.</param>
    [LoggerMessage(
        EventId = BackupFailureId,
        EventName = "RelioBackupFailure",
        Level = LogLevel.Error,
        Message = "Backup job failed with exception type {ExceptionType}.")]
    public static partial void BackupFailure(ILogger logger, string exceptionType);
}
