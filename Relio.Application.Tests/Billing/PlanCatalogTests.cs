using AwesomeAssertions;
using Relio.Application.Billing;
using Xunit;

namespace Relio.Application.Tests.Billing;

public sealed class PlanCatalogTests
{
    [Fact]
    public void Free_plan_allows_twenty_five_active_people()
    {
        PlanCatalog.Free.MaxActivePeople.Should().Be(25);
        PlanCatalog.Get(PlanTier.Free).Should().BeSameAs(PlanCatalog.Free);
    }

    [Fact]
    public void Pro_plan_is_unlimited()
    {
        PlanCatalog.Pro.MaxActivePeople.Should().BeNull();
        PlanCatalog.Get(PlanTier.Pro).Should().BeSameAs(PlanCatalog.Pro);
    }

    [Fact]
    public void Pro_costs_two_dollars_a_month_or_twelve_dollars_a_year()
    {
        PlanCatalog.ProMonthlyPriceUsd.Should().Be(2m);
        PlanCatalog.ProYearlyPriceUsd.Should().Be(12m);
        PlanCatalog.MonthlyPriceText.Should().Be("$2 / month");
        PlanCatalog.YearlyPriceText.Should().Be("$12 / year");
        PlanCatalog.YearlyMonthlyEquivalentText.Should().Be("$1 / month");
    }

    [Fact]
    public void Every_tier_has_a_definition_in_display_order()
    {
        PlanCatalog.All.Select(plan => plan.Tier).Should().Equal(Enum.GetValues<PlanTier>());
    }

    [Fact]
    public void Unknown_tier_is_rejected()
    {
        var act = () => PlanCatalog.Get((PlanTier)42);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Limit_exception_carries_the_limit_and_no_content()
    {
        var exception = new PlanLimitReachedException(25);

        exception.Limit.Should().Be(25);
        exception.Message.Should().Be("The current plan allows up to 25 active people.");
    }
}
