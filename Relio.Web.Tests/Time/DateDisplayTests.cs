using Relio.Web.Time;

namespace Relio.Web.Tests.Time;

public class DateDisplayTests
{
    [Fact]
    public void Omits_the_year_for_the_current_year()
    {
        DateDisplay.Format(new DateOnly(2026, 3, 3), new DateOnly(2026, 10, 6)).Should().Be("3 March");
    }

    [Fact]
    public void Includes_the_year_for_any_other_year()
    {
        DateDisplay.Format(new DateOnly(2025, 3, 3), new DateOnly(2026, 10, 6)).Should().Be("3 March 2025");
        DateDisplay.Format(new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31)).Should().Be("1 January 2027");
    }
}
