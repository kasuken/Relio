using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Profile;
using Relio.Domain;
using Relio.Web.Components.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Settings;

public class DisplayNameSettingsTests
{
    // See ConfirmDialogTests for why the context is created per test with "await using".
    private static BunitContext CreateContext(FakeUserProfileService service)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IUserProfileService>(service);
        context.Render<MudPopoverProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Shows_the_stored_display_name()
    {
        await using var context = CreateContext(new FakeUserProfileService("Emanuele"));

        var cut = context.Render<DisplayNameSettings>();

        cut.Find("input").GetAttribute("value").Should().Be("Emanuele");
    }

    [Fact]
    public async Task Saving_passes_the_entered_name_to_the_service()
    {
        var service = new FakeUserProfileService();
        await using var context = CreateContext(service);
        var cut = context.Render<DisplayNameSettings>();

        cut.Find("input").Input("  Manu  ");
        cut.Find("[data-testid='settings-display-name-save']").Click();

        cut.WaitForAssertion(() => service.DisplayName.Should().Be("Manu"));
    }

    [Fact]
    public async Task Clearing_the_field_and_saving_clears_the_display_name()
    {
        var service = new FakeUserProfileService("Emanuele");
        await using var context = CreateContext(service);
        var cut = context.Render<DisplayNameSettings>();

        cut.Find("input").Input(string.Empty);
        cut.Find("[data-testid='settings-display-name-save']").Click();

        cut.WaitForAssertion(() => service.DisplayName.Should().BeNull());
    }

    [Fact]
    public async Task A_service_validation_failure_shows_an_error()
    {
        var service = new FakeUserProfileService("Emanuele");
        await using var context = CreateContext(service);
        var cut = context.Render<DisplayNameSettings>();

        // MaxLength stops a person typing this much, but the service is the authority; bUnit can
        // still deliver the value.
        cut.Find("input").Input(new string('a', UserProfile.DisplayNameMaxLength + 1));
        cut.Find("[data-testid='settings-display-name-save']").Click();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain($"Keep it to {UserProfile.DisplayNameMaxLength} characters or fewer."));
        service.DisplayName.Should().Be("Emanuele");
    }
}
