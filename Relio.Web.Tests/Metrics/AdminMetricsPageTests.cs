using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Metrics;
using Relio.Web.Components.Pages;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Metrics;

public sealed class AdminMetricsPageTests
{
    [Fact]
    public async Task Enabled_report_shows_aggregate_values_and_definitions_without_user_rows()
    {
        var service = new FakeProductMetricsReportService(new ProductMetricsReport(
            IsEnabled: true,
            LiveAccountCount: 2,
            OwnedPeopleCount: 5,
            AveragePeoplePerAccount: 2.5m,
            CurrentUtcMonthStart: new DateOnly(2026, 3, 1),
            InteractionsThisUtcMonth: 3,
            SavedReminderCount: 4,
            AccountsWithSavedReminders: 1,
            AccountsWithSavedRemindersPercent: 50m,
            EligibleRetentionCohorts: 5,
            ReturnedRetentionCohorts: 3,
            RetentionRatePercent: 60m));
        await using var context = CreateContext(service);

        var cut = context.Render<AdminMetrics>();

        cut.FindAll("h1").Should().ContainSingle();
        cut.Find("[data-testid='metrics-live-accounts']").TextContent.Should().Contain("2");
        cut.Find("[data-testid='metrics-live-accounts']").TextContent
            .Should().Contain("All existing accounts, including disabled accounts");
        cut.Find("[data-testid='metrics-people']").TextContent.Should().Contain("5");
        cut.Find("[data-testid='metrics-average-people']").TextContent.Should().Contain("2.5");
        cut.Find("[data-testid='metrics-interactions']").TextContent.Should().Contain("March 2026");
        cut.Find("[data-testid='metrics-reminders']").TextContent.Should().Contain("4");
        cut.Find("[data-testid='metrics-reminder-accounts']").TextContent.Should().Contain("1 of 2 (50%)");
        cut.Find("[data-testid='metrics-retention-rate']").TextContent.Should().Be("60%");
        cut.Markup.Should().Contain("not on account registration");
        cut.Markup.Should().Contain("current UTC calendar month");
        cut.FindAll("tbody tr").Should().BeEmpty();
        cut.Markup.Should().NotContain("Private account name");
        service.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Disabled_report_shows_status_without_metrics_or_fake_retention()
    {
        var service = new FakeProductMetricsReportService(ProductMetricsReport.Disabled);
        await using var context = CreateContext(service);

        var cut = context.Render<AdminMetrics>();

        cut.Find("[data-testid='metrics-disabled']").TextContent.Should().Contain("Product metrics are off");
        cut.FindAll("[data-testid='metrics-retention-rate']").Should().BeEmpty();
        cut.FindAll("[data-testid='metrics-people']").Should().BeEmpty();
        cut.FindAll("h1").Should().ContainSingle();
        service.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Enabled_report_with_no_completed_cohorts_shows_no_data_instead_of_zero_percent()
    {
        var service = new FakeProductMetricsReportService(new ProductMetricsReport(
            IsEnabled: true,
            LiveAccountCount: 1,
            OwnedPeopleCount: 0,
            AveragePeoplePerAccount: 0m,
            CurrentUtcMonthStart: new DateOnly(2026, 3, 1),
            InteractionsThisUtcMonth: 0,
            SavedReminderCount: 0,
            AccountsWithSavedReminders: 0,
            AccountsWithSavedRemindersPercent: 0m,
            EligibleRetentionCohorts: 0,
            ReturnedRetentionCohorts: 0,
            RetentionRatePercent: null));
        await using var context = CreateContext(service);

        var cut = context.Render<AdminMetrics>();

        cut.Find("[data-testid='metrics-retention-no-data']").TextContent.Should().Be("No completed cohorts yet");
        cut.FindAll("[data-testid='metrics-retention-rate']").Should().BeEmpty();
    }

    private static BunitContext CreateContext(FakeProductMetricsReportService service)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddLogging();
        context.Services.AddSingleton<IProductMetricsReportService>(service);
        return context;
    }

    private sealed class FakeProductMetricsReportService(ProductMetricsReport report) : IProductMetricsReportService
    {
        public int Calls { get; private set; }

        public Task<ProductMetricsReport> GetAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(report);
        }
    }
}
