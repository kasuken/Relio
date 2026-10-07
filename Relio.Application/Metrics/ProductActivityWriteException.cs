namespace Relio.Application.Metrics;

/// <summary>
/// A sanitized failure to record product activity. It deliberately has no database exception or
/// stored value attached.
/// </summary>
public sealed class ProductActivityWriteException()
    : Exception("Product activity could not be recorded.");
