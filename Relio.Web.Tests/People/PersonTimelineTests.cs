using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Interactions;
using Relio.Application.Time;
using Relio.Application.Timeline;
using Relio.Domain;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;
using Relio.Web.Tests.Settings;

namespace Relio.Web.Tests.People;

public sealed class PersonTimelineTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);
    private static readonly DateTime NowUtc = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static BunitContext CreateContext(
        FakePersonTimelineService timeline,
        out IRenderedComponent<MudPopoverProvider> popovers)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPersonTimelineService>(timeline);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("UTC", Today));
        popovers = context.Render<MudPopoverProvider>();
        return context;
    }

    private static PersonTimelineEntry Interaction(Guid id, DateOnly date, string text) =>
        new(id, TimelineEntryKind.Interaction, date, NowUtc, text, InteractionKind.Call, false, []);

    private static PersonTimelineEntry Note(Guid id, DateOnly date, string text, bool pinned = false) =>
        new(id, TimelineEntryKind.Note, date, NowUtc.AddMinutes(-1), text, null, pinned, []);

    [Fact]
    public async Task Load_more_appends_the_next_mixed_page_using_its_continuation()
    {
        var personId = Guid.NewGuid();
        var continuation = new TimelineContinuation(
            new InteractionTimelineCursor(Today.AddDays(-1), NowUtc, Guid.NewGuid()),
            new NoteTimelineCursor(NowUtc.AddMinutes(-1), Guid.NewGuid()));
        var firstInteraction = Interaction(Guid.NewGuid(), Today, "A conversation.");
        var firstNote = Note(Guid.NewGuid(), Today.AddDays(-1), "A remembered detail.", pinned: true);
        var second = Interaction(Guid.NewGuid(), Today.AddDays(-2), "A later conversation.");
        var timeline = new FakePersonTimelineService
        {
            Result = (_, _, cursor, pageSize) => cursor is null
                ? new PersonTimelinePage([firstInteraction, firstNote], pageSize, true, continuation)
                : new PersonTimelinePage([second], pageSize, false, null),
        };
        await using var context = CreateContext(timeline, out _);
        var cut = context.Render<PersonTimeline>(parameters => parameters.Add(component => component.PersonId, personId));

        cut.FindAll("[data-testid='timeline-entry-text']").Select(item => item.TextContent.Trim())
            .Should().Equal("A conversation.", "A remembered detail.");
        cut.Find("[data-testid='timeline-entry-pinned']").TextContent.Should().Contain("Pinned");
        cut.Find("[data-testid='timeline-load-more']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='timeline-entry-text']").Select(item => item.TextContent.Trim())
                .Should().Equal("A conversation.", "A remembered detail.", "A later conversation."));
        timeline.Queries.Should().HaveCount(2);
        timeline.Queries[0].Continuation.Should().BeNull();
        timeline.Queries[1].Continuation.Should().Be(continuation);
        timeline.Queries.Should().OnlyContain(query => query.PersonId == personId);
        cut.FindAll("[data-testid='timeline-load-more']").Should().BeEmpty();
    }

    [Fact]
    public async Task Filter_requests_the_selected_stream_and_shows_a_specific_empty_state()
    {
        var personId = Guid.NewGuid();
        var timeline = new FakePersonTimelineService();
        await using var context = CreateContext(timeline, out var popovers);
        var cut = context.Render<PersonTimeline>(parameters => parameters.Add(component => component.PersonId, personId));

        cut.Find("[data-testid='timeline-filter']").MouseDown();
        popovers.WaitForElements(".mud-list-item")
            .Single(item => item.TextContent.Trim() == "Notes")
            .Click();

        cut.WaitForAssertion(() =>
        {
            timeline.Queries.Last().Filter.Should().Be(TimelineFilter.Note);
            cut.Find("[data-testid='timeline-empty']").TextContent.Should().Contain("No notes have been recorded yet.");
        });

        cut.Find("[data-testid='timeline-filter']").MouseDown();
        popovers.WaitForElements(".mud-list-item")
            .Single(item => item.TextContent.Trim() == "Difficult moments")
            .Click();
        cut.WaitForAssertion(() =>
        {
            timeline.Queries.Last().Filter.Should().Be(TimelineFilter.DifficultMoment);
            cut.Find("[data-testid='timeline-empty']").TextContent
                .Should().Contain("No difficult moments have been recorded yet.");
        });
    }

    [Fact]
    public async Task A_missing_person_uses_the_same_private_not_found_message()
    {
        var timeline = new FakePersonTimelineService
        {
            Result = (_, _, _, _) => null,
        };
        await using var context = CreateContext(timeline, out _);
        var cut = context.Render<PersonTimeline>(parameters => parameters.Add(component => component.PersonId, Guid.NewGuid()));

        cut.Find("[data-testid='timeline-person-missing']").TextContent
            .Should().Contain("This person isn't in your list.");
        cut.Markup.Should().NotContain("does not exist");
    }
}
