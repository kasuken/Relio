using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.DifficultMoments;
using Relio.Application.People;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Components.Pages;
using Relio.Web.Tests.People;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Pages;

public class DifficultMomentsPageTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private static BunitContext CreateContext(
        FakeDifficultMomentService moments,
        FakePeopleService people)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IDifficultMomentService>(moments);
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("UTC", Today));
        context.Render<MudPopoverProvider>();
        return context;
    }

    [Fact]
    public async Task Empty_overview_displays_calm_empty_state()
    {
        var moments = new FakeDifficultMomentService { OverviewItemsToReturn = [] };
        var people = new FakePeopleService();
        await using var context = CreateContext(moments, people);

        var cut = context.Render<Relio.Web.Components.Pages.DifficultMoments>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='moments-empty']").Should().NotBeNull();
            cut.Find("[data-testid='moments-empty']").TextContent.Should().Contain("No difficult moments recorded");
        });
    }

    [Fact]
    public async Task Overview_displays_moments_with_details()
    {
        var personId = Guid.NewGuid();
        var momentId = Guid.NewGuid();
        var moments = new FakeDifficultMomentService
        {
            OverviewItemsToReturn =
            [
                new DifficultMomentOverviewItem(
                    momentId,
                    personId,
                    "Ada Lovelace",
                    false,
                    new DateOnly(2026, 9, 1),
                    "A serious misunderstanding.",
                    "Unclear expectations.",
                    "Clarified responsibilities.",
                    "Document agreement early.",
                    DifficultMomentStatus.Resolved,
                    new DateOnly(2026, 9, 2),
                    null,
                    0,
                    DateTime.UtcNow),
            ],
        };
        var people = new FakePeopleService();
        await using var context = CreateContext(moments, people);

        var cut = context.Render<Relio.Web.Components.Pages.DifficultMoments>();

        cut.WaitForAssertion(() =>
        {
            cut.Find($"[data-testid='moment-card-{momentId}']").Should().NotBeNull();
            cut.Find($"[data-testid='moment-person-{momentId}']").TextContent.Should().Contain("Ada Lovelace");
            cut.Find($"[data-testid='moment-description-{momentId}']").TextContent.Should().Contain("A serious misunderstanding.");
            cut.Find($"[data-testid='moment-trigger-{momentId}']").TextContent.Should().Contain("Unclear expectations.");
            cut.Find($"[data-testid='moment-resolution-{momentId}']").TextContent.Should().Contain("Clarified responsibilities.");
            cut.Find($"[data-testid='moment-lessons-{momentId}']").TextContent.Should().Contain("Document agreement early.");
            cut.Find($"[data-testid='moment-status-{momentId}']").TextContent.Should().Contain("Resolved");
        });
    }

    [Fact]
    public async Task Filter_by_include_archived_reloads_overview()
    {
        var moments = new FakeDifficultMomentService();
        var people = new FakePeopleService();
        await using var context = CreateContext(moments, people);

        var cut = context.Render<Relio.Web.Components.Pages.DifficultMoments>();

        cut.WaitForAssertion(() => moments.LastOverviewFilter.Should().NotBeNull());
        moments.LastOverviewFilter!.IncludeArchived.Should().BeFalse();

        cut.Find("[data-testid='filter-archived']").Change(true);

        cut.WaitForAssertion(() =>
        {
            moments.LastOverviewFilter.Should().NotBeNull();
            moments.LastOverviewFilter!.IncludeArchived.Should().BeTrue();
        });
    }
}
