using Relio.Application.Billing;
using Relio.Web.Components.Billing;
using Relio.Web.Components.Pages;

namespace Relio.Web.Tests.Billing;

public sealed class PlanTextTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    [Fact]
    public void Usage_shows_the_limit_on_the_free_plan_and_unlimited_on_pro()
    {
        PlanText.Usage(Summary(PlanTier.Free, active: 12, max: 25)).Should().Be("12 of 25 active people");
        PlanText.Usage(Summary(PlanTier.Pro, active: 40, max: null)).Should().Be("40 active people (unlimited)");
        PlanText.Usage(Summary(PlanTier.Pro, active: 1, max: null)).Should().Be("1 active person (unlimited)");
    }

    [Fact]
    public void Subscribe_buttons_state_both_prices()
    {
        PlanText.SubscribeYearly.Should().Be("Subscribe yearly - $12 / year ($1 / month)");
        PlanText.SubscribeMonthly.Should().Be("Subscribe monthly - $2 / month");
    }

    [Fact]
    public void Dates_are_the_users_calendar_day_not_the_utc_one()
    {
        // 23:30 UTC on 9 October is already 10 October on Kiritimati (UTC+14).
        var instant = new DateTime(2026, 10, 9, 23, 30, 0, DateTimeKind.Unspecified);
        var kiritimati = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");

        PlanText.Date(instant, TimeZoneInfo.Utc, Today).Should().Be("9 October");
        PlanText.Date(instant, kiritimati, Today).Should().Be("10 October");
        PlanText.Renews(instant.AddYears(1), TimeZoneInfo.Utc, Today).Should().Be("Renews on 9 October 2027.");
    }

    [Fact]
    public void Limit_messages_name_the_limit_and_the_way_out()
    {
        PlanText.LimitReached(25).Should().Contain("25 active people").And.Contain("Archive someone").And.Contain("Relio Pro");
        PlanText.LimitRefused(25).Should().Contain("nothing was saved");
        UserDataPortabilityText.PlanLimit(25).Should().Contain("nothing was imported").And.Contain("Plan and billing");
    }

    private static PlanSummary Summary(PlanTier tier, int active, int? max) =>
        new(true, tier, active, max, null, null, null, false, false);
}
