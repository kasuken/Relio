using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Interactions;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

public class PersonLinkDialogTests
{
    private static readonly Guid PersonId = Guid.NewGuid();

    private static readonly InteractionParticipantOption Marco = new(Guid.NewGuid(), "Marco Rossi", false);
    private static readonly InteractionParticipantOption Grace = new(Guid.NewGuid(), "Grace Hopper", false);
    private static readonly InteractionParticipantOption Archived = new(Guid.NewGuid(), "Old Friend", true);

    private sealed record Harness(
        BunitContext Context,
        IRenderedComponent<MudDialogProvider> Dialogs,
        IRenderedComponent<MudSnackbarProvider> Snackbars,
        FakePersonLinkService Links,
        FakeInteractionService Interactions);

    private static Harness CreateHarness(params InteractionParticipantOption[] candidates)
    {
        var links = new FakePersonLinkService();
        var interactions = new FakeInteractionService { CandidateOptions = candidates };
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPersonLinkService>(links);
        context.Services.AddSingleton<IInteractionService>(interactions);
        context.Render<MudPopoverProvider>();
        var dialogs = context.Render<MudDialogProvider>();
        var snackbars = context.Render<MudSnackbarProvider>();
        return new Harness(context, dialogs, snackbars, links, interactions);
    }

    private static async Task<IDialogReference> OpenAsync(
        Harness harness,
        Guid? linkId = null,
        string? otherName = null,
        PersonLinkKind? kind = null,
        IReadOnlyCollection<Guid>? excluded = null)
    {
        var parameters = new DialogParameters<PersonLinkDialog>
        {
            { x => x.PersonId, PersonId },
            { x => x.PersonFirstName, "Anna" },
            { x => x.LinkId, linkId },
            { x => x.OtherDisplayName, otherName },
            { x => x.InitialKind, kind },
            { x => x.ExcludedPersonIds, excluded ?? [] },
        };
        var service = harness.Context.Services.GetRequiredService<IDialogService>();
        var reference = await service.ShowAsync<PersonLinkDialog>("Connection", parameters);
        harness.Dialogs.WaitForAssertion(() => harness.Dialogs.Find("[data-testid='person-link-form']"));
        return reference;
    }

    private static async Task ChoosePersonAsync(Harness harness, InteractionParticipantOption person)
    {
        harness.Dialogs.WaitForAssertion(() =>
            harness.Dialogs.FindComponent<MudAutocomplete<InteractionParticipantOption>>().Should().NotBeNull());
        var autocomplete = harness.Dialogs.FindComponent<MudAutocomplete<InteractionParticipantOption>>();
        await autocomplete.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(person));
    }

    private static async Task ChooseKindAsync(Harness harness, PersonLinkKind kind)
    {
        var select = harness.Dialogs.FindComponent<MudSelect<PersonLinkKind?>>();
        await select.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(kind));
    }

    [Fact]
    public async Task The_candidates_exclude_the_profile_people_already_linked_and_archived_people()
    {
        var excludedAlready = new InteractionParticipantOption(Guid.NewGuid(), "Already Linked", false);
        var profile = new InteractionParticipantOption(PersonId, "Anna Rossi", false);
        var harness = CreateHarness(profile, Marco, excludedAlready, Archived, Grace);
        await using var context = harness.Context;
        await OpenAsync(harness, excluded: [excludedAlready.PersonId]);

        var autocomplete = harness.Dialogs.FindComponent<MudAutocomplete<InteractionParticipantOption>>();
        var search = autocomplete.Instance.SearchFunc
            ?? throw new InvalidOperationException("The picker must provide a search function.");
        var searchTask = search(string.Empty, CancellationToken.None)
            ?? throw new InvalidOperationException("The picker must return a search task.");
        var options = await searchTask
            ?? throw new InvalidOperationException("The picker must return options.");

        harness.Interactions.CandidateQueries.Should().ContainSingle().Which.ProfilePersonId.Should().Be(Guid.Empty);
        options.Should().Equal(Marco, Grace);
        harness.Dialogs.Find("label").TextContent.Should().Contain("Person");
    }

    [Fact]
    public async Task Choosing_a_person_and_a_kind_sends_the_right_request_and_closes_with_success()
    {
        var harness = CreateHarness(Marco);
        await using var context = harness.Context;
        var reference = await OpenAsync(harness);
        await ChoosePersonAsync(harness, Marco);
        await ChooseKindAsync(harness, PersonLinkKind.Parent);

        harness.Dialogs.Find("[data-testid='person-link-save']").Click();

        var result = await reference.Result;
        result!.Canceled.Should().BeFalse();
        result.Data.Should().Be(true);
        harness.Links.Created.Should().ContainSingle().Which.Should().BeEquivalentTo(new CreatePersonLinkRequest
        {
            PersonId = PersonId,
            RelatedPersonId = Marco.PersonId,
            Kind = PersonLinkKind.Parent,
        });
    }

    [Fact]
    public async Task The_kind_question_names_the_profile_and_lists_every_kind()
    {
        var harness = CreateHarness(Marco);
        await using var context = harness.Context;
        await OpenAsync(harness);

        harness.Dialogs.Markup.Should().Contain("What are they to Anna?");
        harness.Dialogs.FindComponent<MudSelect<PersonLinkKind?>>().FindComponents<MudSelectItem<PersonLinkKind?>>()
            .Count.Should().Be(PersonLinkKinds.All.Count);
    }

    [Fact]
    public async Task Saving_with_nothing_chosen_shows_the_services_messages_next_to_the_fields()
    {
        var harness = CreateHarness(Marco);
        await using var context = harness.Context;
        await OpenAsync(harness);

        harness.Dialogs.Find("[data-testid='person-link-save']").Click();

        harness.Dialogs.WaitForAssertion(() =>
        {
            harness.Dialogs.Find("[data-testid='person-link-person-error']").TextContent.Should().Be("Choose a person.");
            harness.Dialogs.Find("[data-testid='person-link-kind-error']").TextContent.Should().Be("Choose how they are related.");
        });
        harness.Links.Created.Should().BeEmpty("the fake service refused it with the rule codes");
    }

    [Fact]
    public async Task A_pair_that_is_already_linked_shows_the_message_in_an_alert_and_keeps_the_dialog_open()
    {
        var harness = CreateHarness(Marco);
        await using var context = harness.Context;
        await OpenAsync(harness);
        harness.Links.ThrowOnNextCreate = new PersonLinkValidationException([PersonLinkValidationError.AlreadyLinked]);
        await ChoosePersonAsync(harness, Marco);
        await ChooseKindAsync(harness, PersonLinkKind.Friend);

        harness.Dialogs.Find("[data-testid='person-link-save']").Click();

        harness.Dialogs.WaitForAssertion(() =>
            harness.Dialogs.Find("[data-testid='person-link-error']").TextContent
                .Should().Contain("already connected"));
    }

    [Fact]
    public async Task A_person_who_is_gone_shows_a_calm_message_and_reloads_the_candidates()
    {
        var harness = CreateHarness(Marco);
        await using var context = harness.Context;
        await OpenAsync(harness);
        harness.Links.ThrowOnNextCreate = new ForeignEntityNotOwnedException(ForeignEntityNames.People);
        await ChoosePersonAsync(harness, Marco);
        await ChooseKindAsync(harness, PersonLinkKind.Friend);

        harness.Dialogs.Find("[data-testid='person-link-save']").Click();

        harness.Dialogs.WaitForAssertion(() =>
            harness.Dialogs.Find("[data-testid='person-link-error']").TextContent
                .Should().Be(PersonLinkText.PersonGone));
        harness.Interactions.CandidateQueries.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_load_failure_offers_try_again_and_never_shows_the_failure_text()
    {
        var harness = CreateHarness(Marco);
        await using var context = harness.Context;
        harness.Interactions.ThrowOnNextCandidateQuery = new InvalidOperationException("boom Marco Rossi");
        await OpenAsync(harness);

        harness.Dialogs.Find("[data-testid='person-link-load-error']").TextContent
            .Should().Contain(PersonLinkText.CandidatesLoadFailed).And.NotContain("boom");
        harness.Dialogs.Find("[data-testid='person-link-save']").HasAttribute("disabled").Should().BeTrue();

        harness.Dialogs.Find("[data-testid='person-link-load-retry']").Click();

        harness.Dialogs.WaitForAssertion(() =>
            harness.Dialogs.Find("[data-testid='person-link-person-picker']").Should().NotBeNull());
    }

    [Fact]
    public async Task Editing_shows_the_other_person_as_text_without_a_picker_and_sends_an_update_from_the_profile()
    {
        var linkId = Guid.NewGuid();
        var harness = CreateHarness();
        await using var context = harness.Context;
        var reference = await OpenAsync(harness, linkId, "Marco Rossi", PersonLinkKind.Friend);

        harness.Dialogs.Find("[data-testid='person-link-other']").TextContent.Should().Be("Marco Rossi");
        harness.Dialogs.FindAll("[data-testid='person-link-person-picker']").Should().BeEmpty();
        harness.Interactions.CandidateQueries.Should().BeEmpty("nobody is chosen when editing");
        await ChooseKindAsync(harness, PersonLinkKind.Manager);

        harness.Dialogs.Find("[data-testid='person-link-save']").Click();

        var result = await reference.Result;
        result!.Data.Should().Be(true);
        var (updatedId, request) = harness.Links.Updated.Should().ContainSingle().Subject;
        updatedId.Should().Be(linkId);
        request.PersonId.Should().Be(PersonId);
        request.Kind.Should().Be(PersonLinkKind.Manager);
    }

    [Fact]
    public async Task Editing_a_connection_that_is_gone_tells_the_user_and_closes_without_success()
    {
        var harness = CreateHarness();
        await using var context = harness.Context;
        harness.Links.UpdateResult = false;
        var reference = await OpenAsync(harness, Guid.NewGuid(), "Marco Rossi", PersonLinkKind.Friend);

        harness.Dialogs.Find("[data-testid='person-link-save']").Click();

        var result = await reference.Result;
        result!.Canceled.Should().BeFalse();
        result.Data.Should().Be(false);
        harness.Snackbars.WaitForAssertion(() => harness.Snackbars.Markup.Should().Contain(PersonLinkText.Gone));
    }

    [Fact]
    public async Task Cancel_closes_the_dialog_without_calling_the_service()
    {
        var harness = CreateHarness(Marco);
        await using var context = harness.Context;
        var reference = await OpenAsync(harness);

        harness.Dialogs.Find("[data-testid='person-link-cancel']").Click();

        var result = await reference.Result;
        result!.Canceled.Should().BeTrue();
        harness.Links.Created.Should().BeEmpty();
    }
}
