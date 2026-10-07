namespace Relio.Application.Metrics;

/// <summary>
/// Configuration for optional, aggregate-only product metrics (issue #62). Metrics are disabled
/// unless an operator explicitly enables them.
/// </summary>
/// <remarks>
/// Bind this option to <c>HostedFeatures:ProductMetrics</c> and validate it on host startup.
/// The Boolean binding rejects malformed values. Retention is deliberately not configurable:
/// the activity cohort has a fixed 90-day lifetime.
/// </remarks>
public sealed class ProductMetricsOptions
{
    /// <summary>The configuration section containing <see cref="Enabled"/>.</summary>
    public const string SectionName = "HostedFeatures:ProductMetrics";

    /// <summary>Whether product activity may be observed and aggregate metrics reported.</summary>
    public bool Enabled { get; set; }
}
