using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.People;
using Relio.Application.Reminders;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Components.Pages;
using Relio.Web.Tests.People;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Reminders;

public class RemindersPageTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static BunitContext CreateContext(
        FakeReminderService reminders,
        FakePeopleService? people = null)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IReminderService>(reminders);
        context.Services.AddSingleton<IPeopleService>(people ?? new FakePeopleService());
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("Europe/Rome", Today));
        context.Render<MudPopoverProvider>();
        context.Render<MudDialogProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Reminders_page_shows_empty_state_when_no_active_reminders()
    {
        var reminders = new FakeReminderService();
        await using var context = CreateContext(reminders);

        var cut = context.Render<Relio.Web.Components.Pages.Reminders>();

        cut.Find("[data-testid='reminders-empty']").Should().NotBeNull();
    }

    [Fact]
    public async Task Reminders_page_renders_active_reminders()
    {
        var reminders = new FakeReminderService();
        var personId = Guid.NewGuid();
        reminders.Reminders.Add(new ReminderDto(
            Guid.NewGuid(),
            personId,
            "Ada Lovelace",
            "Discuss algorithms",
            Today,
            ReminderFrequency.Weekly,
            null,
            null,
            Today,
            false,
            null,
            null));

        await using var context = CreateContext(reminders);
        var cut = context.Render<Relio.Web.Components.Pages.Reminders>();

        cut.Find("[data-testid='reminders-active-list']").Should().NotBeNull();
        cut.Markup.Should().Contain("Ada Lovelace");
        cut.Markup.Should().Contain("Discuss algorithms");
        cut.Markup.Should().Contain("Due today");
    }

    [Fact]
    public async Task Completing_reminder_marks_it_completed()
    {
        var reminders = new FakeReminderService();
        var id = Guid.NewGuid();
        reminders.Reminders.Add(new ReminderDto(
            id,
            Guid.NewGuid(),
            "Ada",
            "Task 1",
            Today,
            ReminderFrequency.Once,
            null,
            null,
            Today,
            false,
            null,
            null));

        await using var context = CreateContext(reminders);
        var cut = context.Render<Relio.Web.Components.Pages.Reminders>();

        var completeButton = cut.Find($"[data-testid='reminder-complete-{id}']");
        await completeButton.ClickAsync();

        reminders.Reminders.Single(r => r.Id == id).IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Reminders_page_renders_upcoming_birthday_reminders()
    {
        var reminders = new FakeReminderService();
        var personId = Guid.NewGuid();
        reminders.BirthdayReminders.Add(new BirthdayReminderDto(
            personId,
            "Grace Hopper",
            Today.AddDays(5),
            85,
            Today,
            7,
            true,
            5));

        await using var context = CreateContext(reminders);
        var cut = context.Render<Relio.Web.Components.Pages.Reminders>();

        cut.Find("[data-testid='upcoming-birthdays-section']").Should().NotBeNull();
        cut.Find($"[data-testid='birthday-card-{personId}']").Should().NotBeNull();
        cut.Markup.Should().Contain("Grace Hopper");
        cut.Markup.Should().Contain("Turning 85");
        cut.Markup.Should().Contain("In 5 days");
        cut.Markup.Should().Contain("Due");
    }

    [Fact]
    public async Task Reminders_page_renders_overdue_reach_outs_when_present()
    {
        var reminders = new FakeReminderService();
        var personId = Guid.NewGuid();
        reminders.OverdueReachOuts.Add(new ReachOutDto(
            personId,
            "Grace Hopper",
            30,
            Today.AddDays(-35),
            35,
            5));

        await using var context = CreateContext(reminders);
        var cut = context.Render<Relio.Web.Components.Pages.Reminders>();

        cut.Find("[data-testid='reminders-reach-out']").Should().NotBeNull();
        cut.Find($"[data-testid='reach-out-card-{personId}']").Should().NotBeNull();
        cut.Find($"[data-testid='reach-out-contacted-{personId}']").Should().NotBeNull();
        cut.Markup.Should().Contain("Grace Hopper");
        cut.Markup.Should().Contain("Every 30 days");
        cut.Markup.Should().Contain("5 days overdue");
    }

    [Fact]
    public async Task Reminders_page_mark_contacted_removes_person_from_reach_out()
    {
        var reminders = new FakeReminderService();
        var personId = Guid.NewGuid();
        reminders.OverdueReachOuts.Add(new ReachOutDto(
            personId,
            "Grace Hopper",
            30,
            Today.AddDays(-35),
            35,
            5));

        await using var context = CreateContext(reminders);
        var cut = context.Render<Relio.Web.Components.Pages.Reminders>();

        var button = cut.Find($"[data-testid='reach-out-contacted-{personId}']");
        await button.ClickAsync();

        reminders.ContactedPersonIds.Should().Contain(personId);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='reminders-reach-out']").Should().BeEmpty());
    }
}
