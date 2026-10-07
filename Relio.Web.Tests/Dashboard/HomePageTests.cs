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

namespace Relio.Web.Tests.Dashboard;

public class HomePageTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static BunitContext CreateContext(
        FakePeopleService people,
        FakeReminderService reminders)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IReminderService>(reminders);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("Europe/Rome", Today));
        context.Render<MudPopoverProvider>();
        context.Render<MudDialogProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Dashboard_shows_empty_state_when_user_has_no_people()
    {
        var people = new FakePeopleService();
        var reminders = new FakeReminderService();

        await using var context = CreateContext(people, reminders);
        var cut = context.Render<Home>();

        cut.Find("[data-testid='dashboard-empty']").Should().NotBeNull();
    }

    [Fact]
    public async Task Dashboard_shows_caught_up_when_people_exist_but_no_due_reminders()
    {
        var people = new FakePeopleService();
        people.Known.Add(new Person { Id = Guid.NewGuid(), FirstName = "Grace" });
        var reminders = new FakeReminderService();

        await using var context = CreateContext(people, reminders);
        var cut = context.Render<Home>();

        cut.Find("[data-testid='dashboard-reminders-caught-up']").Should().NotBeNull();
    }

    [Fact]
    public async Task Dashboard_renders_due_reminders_when_present()
    {
        var people = new FakePeopleService();
        people.Known.Add(new Person { Id = Guid.NewGuid(), FirstName = "Grace" });
        var reminders = new FakeReminderService();
        var reminderId = Guid.NewGuid();
        reminders.Reminders.Add(new ReminderDto(
            reminderId,
            people.Known[0].Id,
            "Grace Hopper",
            "Call Grace",
            Today,
            ReminderFrequency.Monthly,
            null,
            null,
            Today,
            false,
            null,
            null));

        await using var context = CreateContext(people, reminders);
        var cut = context.Render<Home>();

        cut.Find($"[data-testid='dashboard-reminder-{reminderId}']").Should().NotBeNull();
        cut.Markup.Should().Contain("Call Grace");
        cut.Markup.Should().Contain("Due today");
    }

    [Fact]
    public async Task Dashboard_renders_due_birthday_reminders_when_present()
    {
        var people = new FakePeopleService();
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" };
        people.Known.Add(person);
        var reminders = new FakeReminderService();
        reminders.BirthdayReminders.Add(new BirthdayReminderDto(
            person.Id,
            "Ada Lovelace",
            Today,
            36,
            Today,
            0,
            true,
            0));

        await using var context = CreateContext(people, reminders);
        var cut = context.Render<Home>();

        cut.Find($"[data-testid='dashboard-birthday-{person.Id}']").Should().NotBeNull();
        cut.Markup.Should().Contain("Ada Lovelace");
        cut.Markup.Should().Contain("Turning 36");
        cut.Markup.Should().Contain("Birthday is today!");
    }

    [Fact]
    public async Task Dashboard_renders_overdue_reach_outs_when_present()
    {
        var people = new FakePeopleService();
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" };
        people.Known.Add(person);
        var reminders = new FakeReminderService();
        reminders.OverdueReachOuts.Add(new ReachOutDto(
            person.Id,
            "Ada Lovelace",
            30,
            Today.AddDays(-35),
            35,
            5));

        await using var context = CreateContext(people, reminders);
        var cut = context.Render<Home>();

        cut.Find("[data-testid='dashboard-reach-out']").Should().NotBeNull();
        cut.Find($"[data-testid='dashboard-reach-out-{person.Id}']").Should().NotBeNull();
        cut.Find($"[data-testid='reach-out-contacted-{person.Id}']").Should().NotBeNull();
        cut.Markup.Should().Contain("Ada Lovelace");
        cut.Markup.Should().Contain("Every 30 days");
        cut.Markup.Should().Contain("5 days overdue");
    }

    [Fact]
    public async Task Dashboard_mark_contacted_button_calls_service_and_removes_from_reach_out()
    {
        var people = new FakePeopleService();
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" };
        people.Known.Add(person);
        var reminders = new FakeReminderService();
        reminders.OverdueReachOuts.Add(new ReachOutDto(
            person.Id,
            "Ada Lovelace",
            30,
            Today.AddDays(-35),
            35,
            5));

        await using var context = CreateContext(people, reminders);
        var cut = context.Render<Home>();

        var button = cut.Find($"[data-testid='reach-out-contacted-{person.Id}']");
        await button.ClickAsync();

        reminders.ContactedPersonIds.Should().Contain(person.Id);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='dashboard-reach-out']").Should().BeEmpty());
    }
}
