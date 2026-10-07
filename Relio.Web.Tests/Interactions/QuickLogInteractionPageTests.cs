using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Interactions;
using Relio.Application.Time;
using Relio.Web.Components.Interactions;
using Relio.Web.Components.Pages;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Interactions;

public sealed class QuickLogInteractionPageTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static BunitContext CreateContext(
        QuickLogInteractionService interactions,
        out IRenderedComponent<MudPopoverProvider> popovers)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddLogging();
        context.Services.AddSingleton<IInteractionService>(interactions);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("Pacific/Kiritimati", Today));
        popovers = context.Render<MudPopoverProvider>();
        context.Render<MudDialogProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task No_active_people_shows_a_path_to_add_a_person()
    {
        var interactions = new QuickLogInteractionService();
        await using var context = CreateContext(interactions, out _);
        var cut = context.Render<QuickLogInteraction>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        cut.Find("[data-testid='quick-log-no-people']").Should().NotBeNull();
        cut.FindAll("h1").Should().ContainSingle();
        cut.Find("h1").TextContent.Should().Be("Log an interaction");
        interactions.CandidateQueries.Should().ContainSingle().Which.ProfilePersonId.Should().Be(Guid.Empty);

        await cut.Find("[data-testid='quick-log-no-people'] button").ClickAsync();

        navigation.ToBaseRelativePath(navigation.Uri).Should().Be("people/new");
    }

    [Fact]
    public async Task Quick_log_uses_the_selected_active_person_and_the_users_calendar_today()
    {
        var activePerson = new InteractionParticipantOption(Guid.NewGuid(), "Ada Lovelace", false);
        var archivedPerson = new InteractionParticipantOption(Guid.NewGuid(), "Grace Hopper", true);
        var interactions = new QuickLogInteractionService
        {
            CandidateOptions = [activePerson, archivedPerson],
        };
        await using var context = CreateContext(interactions, out _);
        var cut = context.Render<QuickLogInteraction>();
        var autocomplete = cut.FindComponent<MudAutocomplete<InteractionParticipantOption>>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var searchFunc = autocomplete.Instance.SearchFunc
            ?? throw new InvalidOperationException("The quick-log picker must provide a search function.");
        var searchTask = searchFunc(string.Empty, CancellationToken.None)
            ?? throw new InvalidOperationException("The quick-log picker must return a search task.");
        var options = await searchTask
            ?? throw new InvalidOperationException("The quick-log picker must return search options.");
        options.Should().ContainSingle();
        options.Single().Should().Be(activePerson);
        cut.Find("label").TextContent.Should().Be("Person");

        await autocomplete.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(archivedPerson));
        cut.FindAll("[data-testid='interaction-editor']").Should().BeEmpty();

        autocomplete = cut.FindComponent<MudAutocomplete<InteractionParticipantOption>>();
        await autocomplete.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(activePerson));
        cut.WaitForElement("[data-testid='interaction-editor']").Should().NotBeNull();
        cut.FindAll("[data-testid='interaction-participants-field']").Should().BeEmpty();
        cut.FindAll("h2").Should().BeEmpty();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='interaction-date-field'] input").GetAttribute("value")
                .Should().Be("7 Oct 2026"));

        cut.Find("[data-testid='interaction-description']").Change("Talked about the garden.");
        await cut.Find("[data-testid='interaction-save']").ClickAsync();

        cut.WaitForAssertion(() => interactions.Created.Should().ContainSingle());
        var request = interactions.Created.Single();
        request.ProfilePersonId.Should().Be(activePerson.PersonId);
        request.ParticipantIds.Should().Equal(activePerson.PersonId);
        request.OccurredOn.Should().Be(Today);
        request.Description.Should().Be("Talked about the garden.");
        navigation.ToBaseRelativePath(navigation.Uri).Should().Be($"people/{activePerson.PersonId}");
    }

    [Fact]
    public async Task A_failed_people_load_is_visible_and_can_be_retried()
    {
        var interactions = new QuickLogInteractionService
        {
            CandidateOptions = [new InteractionParticipantOption(Guid.NewGuid(), "Ada Lovelace", false)],
            ThrowOnNextCandidateQuery = new InvalidOperationException("Private details are not displayed."),
        };
        await using var context = CreateContext(interactions, out _);
        var cut = context.Render<QuickLogInteraction>();

        cut.Find("[data-testid='quick-log-load-error']").TextContent
            .Should().Contain("couldn't be loaded");
        cut.Markup.Should().NotContain("Private details are not displayed.");

        await cut.Find("[data-testid='quick-log-retry']").ClickAsync();

        cut.WaitForElement("[data-testid='quick-log-person-picker']").Should().NotBeNull();
        interactions.CandidateQueries.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_failed_save_stays_on_the_quick_log_page_with_a_generic_message()
    {
        var person = new InteractionParticipantOption(Guid.NewGuid(), "Ada Lovelace", false);
        var interactions = new QuickLogInteractionService
        {
            CandidateOptions = [person],
            ThrowOnNextCreate = new InvalidOperationException("Private details are not displayed."),
        };
        await using var context = CreateContext(interactions, out _);
        var cut = context.Render<QuickLogInteraction>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var autocomplete = cut.FindComponent<MudAutocomplete<InteractionParticipantOption>>();
        await autocomplete.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(person));
        cut.WaitForElement("[data-testid='interaction-editor']");
        cut.WaitForElement("[data-testid='interaction-description']");
        cut.Find("[data-testid='interaction-description']").Change("A private interaction.");

        await cut.Find("[data-testid='interaction-save']").ClickAsync();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='interaction-save-error']").TextContent
                .Should().Contain("The interaction couldn't be saved. Try again."));
        cut.Markup.Should().NotContain("Private details are not displayed.");
        navigation.ToBaseRelativePath(navigation.Uri).Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_people_is_blocked_while_the_interaction_is_saving()
    {
        var person = new InteractionParticipantOption(Guid.NewGuid(), "Ada Lovelace", false);
        var otherPerson = new InteractionParticipantOption(Guid.NewGuid(), "Bea Byron", false);
        var completion = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var interactions = new QuickLogInteractionService
        {
            CandidateOptions = [person, otherPerson],
            CreateTask = completion.Task,
        };
        await using var context = CreateContext(interactions, out _);
        var cut = context.Render<QuickLogInteraction>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var autocomplete = cut.FindComponent<MudAutocomplete<InteractionParticipantOption>>();
        await autocomplete.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(person));
        cut.WaitForElement("[data-testid='interaction-description']");
        cut.Find("[data-testid='interaction-description']").Change("A private interaction.");

        var save = cut.Find("[data-testid='interaction-save']").ClickAsync();
        cut.WaitForAssertion(() =>
            cut.FindComponent<MudAutocomplete<InteractionParticipantOption>>().Instance.Disabled.Should().BeTrue());
        autocomplete = cut.FindComponent<MudAutocomplete<InteractionParticipantOption>>();
        autocomplete.Instance.Disabled.Should().BeTrue();
        await autocomplete.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(otherPerson));

        completion.SetResult(Guid.NewGuid());
        await save;

        interactions.Created.Should().ContainSingle().Which.ProfilePersonId.Should().Be(person.PersonId);
        navigation.ToBaseRelativePath(navigation.Uri).Should().Be($"people/{person.PersonId}");
    }
}
