using Relio.Application.Metrics;

namespace Relio.Application.Tests.Metrics;

public sealed class ProductMetricsOptionsTests
{
    [Fact]
    public void Defaults_to_disabled_and_exposes_no_retention_override()
    {
        var options = new ProductMetricsOptions();

        options.Enabled.Should().BeFalse();
        ProductMetricsOptions.SectionName.Should().Be("HostedFeatures:ProductMetrics");
        typeof(ProductMetricsOptions).GetProperties().Select(property => property.Name)
            .Should().Equal(nameof(ProductMetricsOptions.Enabled));
    }
}
