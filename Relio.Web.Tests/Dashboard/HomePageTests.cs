using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Dashboard;
using Relio.Application.Onboarding;
using Relio.Application.Reminders;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Components.Pages;
using Relio.Web.Tests.Onboarding;
using Relio.Web.Tests.People;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Dashboard;

public class HomePageTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static BunitContext CreateContext(
        FakeDashboardService dashboard,
        FakeReminderService reminders,
        out IRenderedComponent<MudSnackbarProvider> snackbars)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddLogging();
        context.Services.AddSingleton<IDashboardService>(dashboard);
        context.Services.AddSingleton<IOnboardingService>(new FakeOnboardingService(isPending: false));
        context.Services.AddSingleton<IReminderService>(reminders);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("Europe/Rome", Today));
        context.Render<MudPopoverProvider>();
        context.Render<MudDialogProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Dashboard_has_one_heading_while_the_snapshot_is_loading()
    {
        var pendingResult = new TaskCompletionSource<DashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dashboard = new FakeDashboardService(Snapshot(activePeople: 0))
        {
            PendingResult = pendingResult.Task,
        };
        await using var context = CreateContext(dashboard, new FakeReminderService(), out _);

        var cut = context.Render<Home>();

        cut.FindAll("h1").Should().ContainSingle();
        cut.Find("[data-testid='dashboard-loading']").Should().NotBeNull();
        pendingResult.SetResult(Snapshot(activePeople: 0));
        cut.WaitForAssertion(() => cut.Find("[data-testid='dashboard-empty']").Should().NotBeNull());
    }

    [Fact]
    public async Task Dashboard_shows_the_empty_account_invitation_and_single_page_heading()
    {
        var dashboard = new FakeDashboardService(Snapshot(activePeople: 0));
        await using var context = CreateContext(dashboard, new FakeReminderService(), out _);

        var cut = context.Render<Home>();

        cut.Find("[data-testid='dashboard-empty']").Should().NotBeNull();
        cut.Markup.Should().Contain("No one here yet");
        cut.Markup.Should().Contain("Adding people comes next.");
        cut.FindAll("h1").Should().ContainSingle();
        dashboard.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Dashboard_explains_when_all_people_are_archived()
    {
        var dashboard = new FakeDashboardService(Snapshot(activePeople: 0, archivedPeople: 2));
        await using var context = CreateContext(dashboard, new FakeReminderService(), out _);

        var cut = context.Render<Home>();

        cut.Find("[data-testid='dashboard-all-archived']").Should().NotBeNull();
        cut.Find("[data-testid='dashboard-overview']").Should().NotBeNull();
        cut.FindAll("h1").Should().ContainSingle();
        cut.Markup.Should().Contain("restore a profile");
    }

    [Fact]
    public async Task Dashboard_shows_helpful_empty_states_for_each_overview_section()
    {
        var dashboard = new FakeDashboardService(Snapshot(activePeople: 1));
        await using var context = CreateContext(dashboard, new FakeReminderService(), out _);

        var cut = context.Render<Home>();

        cut.Find("[data-testid='dashboard-reminders-empty']").Should().NotBeNull();
        cut.Find("[data-testid='dashboard-reminders-caught-up']").Should().NotBeNull();
        cut.Find("[data-testid='dashboard-birthdays-empty']").Should().NotBeNull();
        cut.Find("[data-testid='dashboard-reach-out-empty']").Should().NotBeNull();
        cut.Find("[data-testid='dashboard-interactions-empty']").Should().NotBeNull();
        cut.Find("[data-testid='dashboard-recent-people-empty']").Should().NotBeNull();
        cut.Find("[data-testid='dashboard-view-reminders']").GetAttribute("href").Should().Be("/reminders");
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task Dashboard_renders_each_populated_section_with_person_links_and_user_calendar_dates()
    {
        var personId = Guid.NewGuid();
        var interactionId = Guid.NewGuid();
        var reminderId = Guid.NewGuid();
        var dashboard = new FakeDashboardService(Snapshot(
            activePeople: 1,
            archivedPeople: 1,
            reminders:
            [
                new DashboardReminderItem(
                    reminderId,
                    personId,
                    "Grace Hopper",
                    "Call about the reunion",
                    Today.AddDays(-2),
                    ReminderFrequency.Once,
                    null,
                    null,
                    Today.AddDays(-2)),
            ],
            birthdays:
            [
                new BirthdayReminderDto(personId, "Grace Hopper", Today.AddDays(3), 86, Today.AddDays(3), 0, false, 3),
            ],
            reachOuts:
            [
                new ReachOutDto(personId, "Grace Hopper", 30, Today.AddDays(-35), 35, 5),
            ],
            interactions:
            [
                new DashboardInteractionItem(
                    interactionId,
                    Today.AddDays(-1),
                    InteractionKind.Call,
                    "She told me about the garden.",
                    [new DashboardInteractionParticipant(personId, "Grace Hopper")]),
            ],
            recentlyAdded:
            [
                new DashboardPersonItem(personId, "Grace Hopper", Today),
            ]));
        await using var context = CreateContext(dashboard, new FakeReminderService(), out _);

        var cut = context.Render<Home>();

        cut.Find($"[data-testid='dashboard-reminder-{reminderId}']").Should().NotBeNull();
        cut.Find($"[data-testid='dashboard-reminder-{reminderId}'] a")
            .GetAttribute("href").Should().Be($"/people/{personId}");
        cut.Markup.Should().Contain("2 days overdue");
        cut.Find($"[data-testid='dashboard-birthday-{personId}']").Should().NotBeNull();
        cut.Markup.Should().Contain("Turns 86 in 3 days");
        cut.Find($"[data-testid='dashboard-reach-out-{personId}']").Should().NotBeNull();
        cut.Markup.Should().Contain("5 days overdue");
        cut.Find($"[data-testid='dashboard-interaction-{interactionId}']").Should().NotBeNull();
        cut.Markup.Should().Contain("She told me about the garden.");
        cut.Find($"[data-testid='dashboard-interaction-{interactionId}'] a")
            .GetAttribute("href").Should().Be($"/people/{personId}");
        cut.Find($"[data-testid='dashboard-recent-person-{personId}'] a")
            .GetAttribute("href").Should().Be($"/people/{personId}");
        cut.Find("[data-testid='dashboard-add-person']").GetAttribute("href").Should().Be("/people/new");
        cut.Find("[data-testid='dashboard-log-interaction']").GetAttribute("href").Should().Be("/interactions/new");
        cut.Find("[data-testid='dashboard-view-reminders']").GetAttribute("href").Should().Be("/reminders");
        cut.Find("[data-testid='dashboard-recent-people'] a[href='/people?sort=added']").Should().NotBeNull();
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task Dashboard_keeps_reminder_actions_and_refreshes_after_marking_a_person_contacted()
    {
        var personId = Guid.NewGuid();
        var reachOut = new ReachOutDto(personId, "Ada Lovelace", 30, Today.AddDays(-35), 35, 5);
        var reminders = new FakeReminderService();
        reminders.OverdueReachOuts.Add(reachOut);
        var initial = Snapshot(activePeople: 1, reachOuts: [reachOut]);
        var dashboard = new FakeDashboardService(initial)
        {
            OnGet = () => initial with { ReachOuts = reminders.OverdueReachOuts.ToArray() },
        };
        await using var context = CreateContext(dashboard, reminders, out var snackbars);

        var cut = context.Render<Home>();
        cut.Find($"[data-testid='reach-out-contacted-{personId}']").Should().NotBeNull();

        await cut.Find($"[data-testid='reach-out-contacted-{personId}']").ClickAsync();

        reminders.ContactedPersonIds.Should().Contain(personId);
        dashboard.Calls.Should().Be(2);
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Marked as contacted"));
        cut.WaitForAssertion(() =>
            cut.FindAll($"[data-testid='dashboard-reach-out-{personId}']").Should().BeEmpty());
    }

    [Fact]
    public async Task Dashboard_refreshes_after_completion_reports_a_stale_reminder()
    {
        var personId = Guid.NewGuid();
        var reminderId = Guid.NewGuid();
        var initial = Snapshot(
            activePeople: 1,
            reminders:
            [
                new DashboardReminderItem(
                    reminderId,
                    personId,
                    "Ada Lovelace",
                    "A reminder that has gone away",
                    Today,
                    ReminderFrequency.Once,
                    null,
                    null,
                    Today),
            ]);
        var dashboard = new FakeDashboardService(initial);
        dashboard.OnGet = () => dashboard.Calls == 1
            ? initial
            : initial with { UpcomingReminders = [] };
        var reminders = new FakeReminderService();
        await using var context = CreateContext(dashboard, reminders, out var snackbars);

        var cut = context.Render<Home>();
        await cut.Find($"[data-testid='dashboard-complete-{reminderId}']").ClickAsync();

        dashboard.Calls.Should().Be(2);
        cut.FindAll($"[data-testid='dashboard-reminder-{reminderId}']").Should().BeEmpty();
        snackbars.WaitForAssertion(() =>
            snackbars.Markup.Should().Contain("This reminder is no longer in your list."));
    }

    private static DashboardSnapshot Snapshot(
        int activePeople,
        int archivedPeople = 0,
        IReadOnlyList<DashboardReminderItem>? reminders = null,
        IReadOnlyList<BirthdayReminderDto>? birthdays = null,
        IReadOnlyList<ReachOutDto>? reachOuts = null,
        IReadOnlyList<DashboardInteractionItem>? interactions = null,
        IReadOnlyList<DashboardPersonItem>? recentlyAdded = null) =>
        new(
            Today,
            activePeople,
            archivedPeople,
            reminders ?? [],
            birthdays ?? [],
            reachOuts ?? [],
            interactions ?? [],
            recentlyAdded ?? []);
}
