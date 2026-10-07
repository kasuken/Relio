using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Interactions;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;
using Relio.Web.Tests.Settings;

namespace Relio.Web.Tests.People;

public sealed class InteractionEditorTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private static BunitContext CreateContext(
        FakeInteractionService interactions,
        out IRenderedComponent<MudPopoverProvider> popovers)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IInteractionService>(interactions);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("UTC", Today));
        popovers = context.Render<MudPopoverProvider>();
        return context;
    }

    private static InteractionParticipantOption Option(Guid id, string name, bool archived = false) =>
        new(id, name, archived);

    private static void Choose(
        IRenderedComponent<InteractionEditor> cut,
        IRenderedComponent<MudPopoverProvider> popovers,
        string field,
        string option)
    {
        cut.Find($"[data-testid='{field}']").MouseDown();
        var items = popovers.WaitForElements(".mud-list-item");
        items.Single(item => item.TextContent.Trim() == option).Click();
    }

    [Fact]
    public async Task Creating_sends_the_complete_shared_participant_list_and_raises_saved()
    {
        var personId = Guid.NewGuid();
        var secondPersonId = Guid.NewGuid();
        var interactions = new FakeInteractionService
        {
            CandidateOptions =
            [
                Option(personId, "Ada Lovelace"),
                Option(secondPersonId, "Bea Byron"),
            ],
        };
        await using var context = CreateContext(interactions, out var popovers);
        var saved = false;
        var cut = context.Render<InteractionEditor>(parameters => parameters
            .Add(component => component.PersonId, personId)
            .Add(component => component.Saved, () => saved = true));

        Choose(cut, popovers, "interaction-kind", "Meeting");
        Choose(cut, popovers, "interaction-participants", "Bea Byron");
        cut.Find("[data-testid='interaction-description']").Change("Talked about the garden.");
        cut.Find("[data-testid='interaction-save']").Click();

        cut.WaitForAssertion(() => interactions.Created.Should().ContainSingle());
        var request = interactions.Created.Single();
        request.ProfilePersonId.Should().Be(personId);
        request.OccurredOn.Should().Be(Today);
        request.Kind.Should().Be(InteractionKind.Meeting);
        request.Description.Should().Be("Talked about the garden.");
        request.ParticipantIds.Should().BeEquivalentTo(new[] { personId, secondPersonId });
        saved.Should().BeTrue();
    }

    [Fact]
    public async Task Editing_preserves_existing_archived_participants_and_replaces_the_full_list()
    {
        var profilePersonId = Guid.NewGuid();
        var archivedPersonId = Guid.NewGuid();
        var interactionId = Guid.NewGuid();
        var interactions = new FakeInteractionService
        {
            Known =
            {
                new InteractionDetails(
                    interactionId,
                    Today.AddDays(-1),
                    InteractionKind.Call,
                    "A private conversation.",
                    DateTime.SpecifyKind(new DateTime(2026, 10, 5, 9, 0, 0), DateTimeKind.Utc),
                    [
                        new InteractionParticipantDetails(profilePersonId, "Ada Lovelace", false),
                        new InteractionParticipantDetails(archivedPersonId, "Grace Hopper", true),
                    ]),
            },
            CandidateOptions =
            [
                Option(profilePersonId, "Ada Lovelace"),
                Option(archivedPersonId, "Grace Hopper", archived: true),
            ],
        };
        await using var context = CreateContext(interactions, out _);
        var cut = context.Render<InteractionEditor>(parameters => parameters
            .Add(component => component.PersonId, profilePersonId)
            .Add(component => component.InteractionId, interactionId));

        interactions.CandidateQueries.Should().ContainSingle();
        interactions.CandidateQueries.Single().IncludedParticipantIds
            .Should().BeEquivalentTo(new[] { profilePersonId, archivedPersonId });
        cut.Find("[data-testid='interaction-save']").Click();

        cut.WaitForAssertion(() => interactions.Updated.Should().ContainSingle());
        var update = interactions.Updated.Single();
        update.InteractionId.Should().Be(interactionId);
        update.Request.Description.Should().Be("A private conversation.");
        update.Request.ParticipantIds.Should().BeEquivalentTo(new[] { profilePersonId, archivedPersonId });
        update.Request.OccurredOn.Should().Be(Today.AddDays(-1));
    }

    [Fact]
    public async Task Editing_uses_a_stable_date_format_when_host_culture_differs()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var personId = Guid.NewGuid();
            var interactionId = Guid.NewGuid();
            var interactions = new FakeInteractionService
            {
                Known =
                {
                    new InteractionDetails(
                        interactionId,
                        Today.AddDays(-1),
                        InteractionKind.Call,
                        "A private conversation.",
                        DateTime.SpecifyKind(new DateTime(2026, 10, 5, 9, 0, 0), DateTimeKind.Utc),
                        [new InteractionParticipantDetails(personId, "Ada Lovelace", false)]),
                },
                CandidateOptions = [Option(personId, "Ada Lovelace")],
            };
            await using var context = CreateContext(interactions, out _);
            var cut = context.Render<InteractionEditor>(parameters => parameters
                .Add(component => component.PersonId, personId)
                .Add(component => component.InteractionId, interactionId));

            cut.WaitForAssertion(() =>
            {
                cut.Find("[data-testid='interaction-date-field'] input")
                    .GetAttribute("value")
                    .Should()
                    .Be("5 Oct 2026");
            });
            cut.Find("[data-testid='interaction-date-field'] input").Blur();
            cut.Find("[data-testid='interaction-save']").Click();

            cut.WaitForAssertion(() => interactions.Updated.Should().ContainSingle());
            interactions.Updated.Single().Request.OccurredOn.Should().Be(Today.AddDays(-1));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task Validation_codes_are_displayed_under_their_own_fields()
    {
        var personId = Guid.NewGuid();
        var interactions = new FakeInteractionService
        {
            ThrowOnNextCreate = new InteractionValidationException(
            [
                InteractionValidationError.DateInFuture,
                InteractionValidationError.ParticipantsRequired,
                InteractionValidationError.DescriptionRequired,
            ]),
        };
        await using var context = CreateContext(interactions, out _);
        var cut = context.Render<InteractionEditor>(parameters => parameters.Add(component => component.PersonId, personId));

        cut.Find("[data-testid='interaction-save']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='interaction-date-field']").TextContent.Should().Contain("The date can't be in the future.");
            cut.Find("[data-testid='interaction-participants-field']").TextContent.Should().Contain("Choose at least one person.");
            cut.Find("[data-testid='interaction-description-field']").TextContent.Should().Contain("Add a short description.");
        });
        interactions.Created.Should().BeEmpty("validation failures are not saved");
    }

    [Fact]
    public void Every_validation_code_has_a_field_message()
    {
        Enum.GetValues<InteractionValidationError>()
            .Select(InteractionFormMessages.Message)
            .Should()
            .OnlyContain(message => !string.IsNullOrWhiteSpace(message));
    }
}
