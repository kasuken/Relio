using Bunit;
using Relio.Web.Components.Layout;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Layout;

public class NavMenuTests : BunitContext
{
    public NavMenuTests() => this.UseMudBlazor();

    [Theory]
    [InlineData("Dashboard", "/")]
    [InlineData("People", "/people")]
    [InlineData("Reminders", "/reminders")]
    [InlineData("Difficult moments", "/difficult-moments")]
    [InlineData("Settings", "/settings")]
    public void Links_to_every_shell_route(string label, string href)
    {
        var cut = Render<NavMenu>();

        var link = cut.FindAll("a").Single(a => a.TextContent.Trim() == label);

        link.GetAttribute("href").Should().Be(href);
    }

    [Fact]
    public void Renders_exactly_the_five_shell_routes()
    {
        var cut = Render<NavMenu>();

        cut.FindAll("a").Should().HaveCount(5);
    }
}
