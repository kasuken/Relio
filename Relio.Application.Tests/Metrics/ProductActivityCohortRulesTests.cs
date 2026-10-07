using Microsoft.Extensions.Time.Testing;
using Relio.Application.Metrics;
using Relio.Domain;

namespace Relio.Application.Tests.Metrics;

public sealed class ProductActivityCohortRulesTests
{
    private static readonly DateOnly CohortStart = new(2026, 1, 1);

    [Fact]
    public void UtcToday_uses_the_utc_date_not_the_provider_offset_date()
    {
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.FromHours(-11)));

        ProductActivityCohortRules.UtcToday(timeProvider).Should().Be(new DateOnly(2026, 1, 2));
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(59, true)]
    [InlineData(60, false)]
    public void IsWithinReturnWindow_includes_only_UTC_days_30_through_59(int ageInDays, bool expected)
    {
        var timeProvider = TimeAtCohortAge(ageInDays);

        ProductActivityCohortRules.IsWithinReturnWindow(CohortStart, ProductActivityCohortRules.UtcToday(timeProvider))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(89, true)]
    [InlineData(90, false)]
    public void IsCompletedCohort_includes_only_UTC_ages_60_through_89(int ageInDays, bool expected)
    {
        var timeProvider = TimeAtCohortAge(ageInDays);

        ProductActivityCohortRules.IsCompletedCohort(CohortStart, ProductActivityCohortRules.UtcToday(timeProvider))
            .Should().Be(expected);
    }

    [Fact]
    public void Retention_expires_at_midnight_UTC_on_day_90()
    {
        var expiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(CohortStart);
        var activity = new ProductActivity { RetentionExpiresAtUtc = expiresAtUtc };
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(expiresAtUtc.AddTicks(-1), TimeSpan.Zero));

        ProductActivityCohortRules.IsExpired(activity, timeProvider.GetUtcNow().UtcDateTime).Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromTicks(1));

        ProductActivityCohortRules.IsExpired(activity, timeProvider.GetUtcNow().UtcDateTime).Should().BeTrue();
        expiresAtUtc.Should().Be(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static FakeTimeProvider TimeAtCohortAge(int ageInDays) =>
        new(new DateTimeOffset(CohortStart.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)
            .AddDays(ageInDays));
}
