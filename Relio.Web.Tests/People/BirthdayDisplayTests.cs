using System.Globalization;
using Relio.Domain;
using Relio.Web.Time;

namespace Relio.Web.Tests.People;

public class BirthdayDisplayTests
{
    [Fact]
    public void Writes_day_month_name_and_year()
    {
        BirthdayDisplay.Format(Birthday.Create(12, 10, 1815)).Should().Be("10 December 1815");
    }

    [Fact]
    public void Leaves_the_year_out_when_it_is_not_known()
    {
        BirthdayDisplay.Format(Birthday.Create(3, 14)).Should().Be("14 March");
    }

    [Fact]
    public void Does_not_pad_a_short_year()
    {
        BirthdayDisplay.Format(Birthday.Create(1, 1, 815)).Should().Be("1 January 815");
    }

    [Fact]
    public void Writes_29_February()
    {
        BirthdayDisplay.Format(Birthday.Create(2, 29)).Should().Be("29 February");
        BirthdayDisplay.Format(Birthday.Create(2, 29, 1992)).Should().Be("29 February 1992");
    }

    [Fact]
    public void Always_uses_English_month_names_whatever_the_server_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");

            BirthdayDisplay.Format(Birthday.Create(12, 10)).Should().Be("10 December");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
