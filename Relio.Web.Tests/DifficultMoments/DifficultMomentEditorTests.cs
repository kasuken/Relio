using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using Relio.Application.DifficultMoments;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Components.DifficultMoments;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.DifficultMoments;

public class DifficultMomentEditorTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private static BunitContext CreateContext(
        FakeDifficultMomentService service,
        out IRenderedComponent<MudSnackbarProvider> snackbars)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IDifficultMomentService>(service);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("UTC", Today));
        context.Services.AddSingleton(NullLogger<DifficultMomentEditor>.Instance);
        context.Render<MudPopoverProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Create_mode_saves_moment_and_invokes_saved_callback()
    {
        var personId = Guid.NewGuid();
        var service = new FakeDifficultMomentService();
        await using var context = CreateContext(service, out var snackbars);
        var saved = false;

        var cut = context.Render<DifficultMomentEditor>(parameters => parameters
            .Add(c => c.PersonId, personId)
            .Add(c => c.Saved, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Find("[data-testid='moment-description-field'] textarea").Input("A difficult disagreement.");
        cut.Find("[data-testid='moment-trigger-field'] textarea").Input("High pressure.");
        cut.Find("[data-testid='moment-resolution-field'] textarea").Input("Talked it through.");
        cut.Find("[data-testid='moment-lessons-field'] textarea").Input("Take a break next time.");

        cut.Find("[data-testid='moment-save']").Click();

        cut.WaitForAssertion(() =>
        {
            service.Created.Should().ContainSingle();
            saved.Should().BeTrue();
        });

        var created = service.Created.Single();
        created.PersonId.Should().Be(personId);
        created.Description.Should().Be("A difficult disagreement.");
        created.Trigger.Should().Be("High pressure.");
        created.Resolution.Should().Be("Talked it through.");
        created.LessonsLearned.Should().Be("Take a break next time.");
        created.Status.Should().Be(DifficultMomentStatus.Open);
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Difficult moment saved"));
    }

    [Fact]
    public async Task Edit_mode_loads_and_updates_the_requested_moment()
    {
        var personId = Guid.NewGuid();
        var momentId = Guid.NewGuid();
        var service = new FakeDifficultMomentService
        {
            MomentToReturn = new DifficultMomentDetails(
                momentId,
                personId,
                "Ada Lovelace",
                false,
                Today.AddDays(-2),
                "Existing description.",
                "Trigger text",
                "Resolution text",
                "Lessons text",
                DifficultMomentStatus.Open,
                null,
                null,
                null,
                [],
                DateTime.UtcNow,
                DateTime.UtcNow),
        };
        await using var context = CreateContext(service, out var snackbars);
        var saved = false;

        var cut = context.Render<DifficultMomentEditor>(parameters => parameters
            .Add(c => c.PersonId, personId)
            .Add(c => c.MomentId, momentId)
            .Add(c => c.Saved, EventCallback.Factory.Create(this, () => saved = true)));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Existing description."));

        cut.Find("[data-testid='moment-description-field'] textarea").Input("Updated description.");
        cut.Find("[data-testid='moment-save']").Click();

        cut.WaitForAssertion(() =>
        {
            service.Updated.Should().ContainSingle();
            saved.Should().BeTrue();
        });

        var (id, request) = service.Updated.Single();
        id.Should().Be(momentId);
        request.Description.Should().Be("Updated description.");
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Difficult moment saved"));
    }

    [Fact]
    public async Task Validation_errors_from_service_are_displayed()
    {
        var personId = Guid.NewGuid();
        var service = new FakeDifficultMomentService
        {
            ThrowOnCreate = new DifficultMomentValidationException([DifficultMomentValidationError.DescriptionRequired]),
        };
        await using var context = CreateContext(service, out _);

        var cut = context.Render<DifficultMomentEditor>(parameters => parameters
            .Add(c => c.PersonId, personId));

        cut.Find("[data-testid='moment-save']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='moment-description-error']").TextContent.Should().Be("Describe what happened.");
        });
    }

    [Fact]
    public async Task Unavailable_moment_shows_unavailable_message()
    {
        var personId = Guid.NewGuid();
        var momentId = Guid.NewGuid();
        var service = new FakeDifficultMomentService
        {
            MomentToReturn = null,
        };
        await using var context = CreateContext(service, out _);

        var cut = context.Render<DifficultMomentEditor>(parameters => parameters
            .Add(c => c.PersonId, personId)
            .Add(c => c.MomentId, momentId));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='moment-editor-unavailable']").TextContent.Should().Contain("This difficult moment is no longer available.");
        });
    }
}
