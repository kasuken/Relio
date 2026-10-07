using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Authorization;
using Relio.Web.Components.Layout;
using Relio.Web.Identity;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Layout;

public class NavMenuTests : BunitContext
{
    private readonly BunitAuthorizationContext _authorization;

    public NavMenuTests()
    {
        this.UseMudBlazor();

        // NavMenu contains an AuthorizeView (the administrator-only link), which needs one.
        _authorization = AddAuthorization();
        _authorization.SetAuthorized("user@example.com");
    }

    private IRenderedComponent<CascadingAuthenticationState> RenderNavMenu() =>
        Render<CascadingAuthenticationState>(parameters => parameters.AddChildContent<NavMenu>());

    [Theory]
    [InlineData("Dashboard", "/dashboard")]
    [InlineData("People", "/people")]
    [InlineData("Reminders", "/reminders")]
    [InlineData("Difficult moments", "/difficult-moments")]
    [InlineData("Settings", "/settings")]
    public void Links_to_every_shell_route(string label, string href)
    {
        var cut = RenderNavMenu();

        var link = cut.FindAll("a").Single(a => a.TextContent.Trim() == label);

        link.GetAttribute("href").Should().Be(href);
    }

    [Fact]
    public void Renders_exactly_the_five_shell_routes_for_non_administrators()
    {
        var cut = RenderNavMenu();

        cut.FindAll("a").Should().HaveCount(5);
        cut.FindAll("a").Should().NotContain(a => a.TextContent.Trim() == "Administration");
    }

    [Fact]
    public void Administrators_also_see_the_administration_link()
    {
        _authorization.SetPolicies(RelioPolicies.Administrator);

        var cut = RenderNavMenu();

        cut.FindAll("a").Should().HaveCount(6);
        cut.FindAll("a").Single(a => a.TextContent.Trim() == "Administration")
            .GetAttribute("href").Should().Be("/admin/users");
    }
}
