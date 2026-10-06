using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Time;
using Relio.Web.Components.Settings;
using Relio.Web.Tests.Shared;
using Relio.Web.Time;

namespace Relio.Web.Tests.Settings;

public class TimeZoneSettingsTests
{
    // See ConfirmDialogTests for why the context is created per test with "await using".
    private static BunitContext CreateContext(FakeUserTimeZoneService service, string? browserTimeZoneId)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IUserTimeZoneService>(service);
        context.Services.AddSingleton<IBrowserTimeZoneReader>(new FakeBrowserTimeZoneReader(browserTimeZoneId));
        context.Render<MudPopoverProvider>();
        return context;
    }

    [Fact]
    public async Task Shows_the_stored_time_zone()
    {
        await using var context = CreateContext(new FakeUserTimeZoneService("America/New_York"), browserTimeZoneId: null);

        var cut = context.Render<TimeZoneSettings>();

        cut.Find("input").GetAttribute("value").Should().Be("America/New_York");
    }

    [Fact]
    public async Task Suggests_the_browser_time_zone_when_it_differs()
    {
        await using var context = CreateContext(new FakeUserTimeZoneService("UTC"), "Pacific/Kiritimati");

        var cut = context.Render<TimeZoneSettings>();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='settings-timezone-browser-suggestion']").TextContent
                .Should().Contain("Pacific/Kiritimati"));
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData(null)]
    [InlineData("Not/AZone")]
    public async Task Does_not_suggest_when_the_browser_zone_matches_is_missing_or_unknown(string? browserTimeZoneId)
    {
        await using var context = CreateContext(new FakeUserTimeZoneService("UTC"), browserTimeZoneId);

        var cut = context.Render<TimeZoneSettings>();

        // OnAfterRenderAsync has run by the time Render returns (the fake completes synchronously).
        cut.FindAll("[data-testid='settings-timezone-browser-suggestion']").Should().BeEmpty();
    }

    [Fact]
    public async Task Using_the_suggestion_then_saving_sets_the_browser_time_zone()
    {
        var service = new FakeUserTimeZoneService("UTC");
        await using var context = CreateContext(service, "Pacific/Kiritimati");
        var cut = context.Render<TimeZoneSettings>();
        cut.WaitForElement("[data-testid='settings-timezone-use-browser']").Click();

        cut.Find("[data-testid='settings-timezone-save']").Click();

        cut.WaitForAssertion(() => service.Saved.Should().Equal("Pacific/Kiritimati"));
        // The suggestion is gone once the saved zone matches the browser's.
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='settings-timezone-browser-suggestion']").Should().BeEmpty());
    }

    [Fact]
    public async Task Choosing_the_suggestion_does_not_save_anything_by_itself()
    {
        var service = new FakeUserTimeZoneService("UTC");
        await using var context = CreateContext(service, "Pacific/Kiritimati");
        var cut = context.Render<TimeZoneSettings>();

        cut.WaitForElement("[data-testid='settings-timezone-use-browser']").Click();

        service.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_time_zone_shows_an_error_and_is_not_saved()
    {
        var service = new FakeUserTimeZoneService("Europe/Rome");
        await using var context = CreateContext(service, browserTimeZoneId: null);
        var cut = context.Render<TimeZoneSettings>();

        cut.Find("input").Input("Not/AZone");
        cut.Find("[data-testid='settings-timezone-save']").Click();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("Choose a time zone from the list, for example Europe/Rome."));
        service.Saved.Should().BeEmpty();
        service.TimeZoneId.Should().Be("Europe/Rome");
    }
}
