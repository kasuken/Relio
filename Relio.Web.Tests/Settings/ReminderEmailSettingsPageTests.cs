using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor;
using Relio.Application.Reminders;
using Relio.Domain;
using Relio.Web.Components.Settings;
using Relio.Web.Email;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Settings;

public class ReminderEmailSettingsPageTests
{
    private static BunitContext CreateContext(
        FakeNotificationPreferencesService preferencesService,
        out IRenderedComponent<MudSnackbarProvider> snackbars,
        string emailProvider = EmailOptions.SmtpProvider)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<INotificationPreferencesService>(preferencesService);
        context.Services.AddSingleton(Options.Create(new EmailOptions { Provider = emailProvider }));
        context.Render<MudPopoverProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Shows_alert_when_email_provider_is_none()
    {
        await using var context = CreateContext(new FakeNotificationPreferencesService(), out _, EmailOptions.NoneProvider);

        var cut = context.Render<ReminderEmailSettings>();

        cut.Find("[data-testid='settings-email-provider-alert']").Should().NotBeNull();
        cut.Markup.Should().Contain("Email delivery is not configured");
    }

    [Fact]
    public async Task Hides_alert_when_email_provider_is_smtp()
    {
        await using var context = CreateContext(new FakeNotificationPreferencesService(), out _, EmailOptions.SmtpProvider);

        var cut = context.Render<ReminderEmailSettings>();

        cut.FindAll("[data-testid='settings-email-provider-alert']").Should().BeEmpty();
    }

    [Fact]
    public async Task Back_link_points_to_settings()
    {
        await using var context = CreateContext(new FakeNotificationPreferencesService(), out _);

        var cut = context.Render<ReminderEmailSettings>();

        cut.Find("[data-testid='settings-back']").GetAttribute("href").Should().Be("/settings");
    }

    [Fact]
    public async Task Loads_stored_preferences_and_saves_updated_preferences()
    {
        var initial = new NotificationPreferencesDto(
            ReminderEmailDelivery.DailyDigest,
            BirthdayRemindersEnabled: true,
            DefaultBirthdayLeadDays: 0,
            UnsubscribeToken: "token-123");
        var service = new FakeNotificationPreferencesService(initial);
        await using var context = CreateContext(service, out var snackbars);

        var cut = context.Render<ReminderEmailSettings>();

        // Find delivery options
        cut.Find("[data-testid='settings-delivery-digest']").Should().NotBeNull();
        cut.Find("[data-testid='settings-delivery-immediate']").Should().NotBeNull();
        cut.Find("[data-testid='settings-delivery-none']").Should().NotBeNull();

        // Click save
        cut.Find("[data-testid='settings-reminders-save']").Click();

        cut.WaitForAssertion(() =>
        {
            service.Saved.Should().ContainSingle();
            service.Saved[0].Delivery.Should().Be(ReminderEmailDelivery.DailyDigest);
            service.Saved[0].BirthdayRemindersEnabled.Should().BeTrue();
            service.Saved[0].DefaultBirthdayLeadDays.Should().Be(0);
        });

        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Reminder preferences saved"));
    }
}
