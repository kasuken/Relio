using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Interactions;
using Relio.Application.Onboarding;
using Relio.Application.People;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Tests.People;
using Relio.Web.Tests.Shared;
using Relio.Web.Tests.Settings;
using Relio.Web.Time;
using OnboardingPage = Relio.Web.Components.Pages.Onboarding;

namespace Relio.Web.Tests.Onboarding;

public sealed class OnboardingPageTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public async Task Time_zone_suggestion_requires_an_explicit_save_before_advancing()
    {
        var onboarding = new FakeOnboardingService();
        var timeZone = new FakeUserTimeZoneService("UTC", Today);
        await using var context = CreateContext(onboarding, timeZone, browserTimeZoneId: "Pacific/Kiritimati");

        var cut = context.Render<OnboardingPage>();

        cut.WaitForElement("[data-testid='onboarding-timezone-browser-suggestion']");
        cut.Find("[data-testid='onboarding-timezone-use-browser']").Click();

        timeZone.Saved.Should().BeEmpty();
        cut.FindAll("[data-testid='onboarding-person-step']").Should().BeEmpty();

        cut.Find("[data-testid='onboarding-timezone-save']").Click();

        cut.WaitForElement("[data-testid='onboarding-person-step']");
        timeZone.Saved.Should().Equal("Pacific/Kiritimati");
        onboarding.Dismissals.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_time_zone_does_not_save_or_dismiss_onboarding()
    {
        var onboarding = new FakeOnboardingService();
        var timeZone = new FakeUserTimeZoneService("Europe/Rome", Today);
        await using var context = CreateContext(onboarding, timeZone);

        var cut = context.Render<OnboardingPage>();
        cut.Find("input").Input("Not/AZone");
        cut.Find("[data-testid='onboarding-timezone-save']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Choose a time zone from the list, for example Europe/Rome.");
            onboarding.Dismissals.Should().Be(0);
        });
        timeZone.Saved.Should().BeEmpty();
        cut.FindAll("[data-testid='onboarding-person-step']").Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_active_person_is_offered_for_the_first_interaction_after_reload()
    {
        var onboarding = new FakeOnboardingService();
        var people = new FakePeopleService();
        var existingPersonId = Guid.NewGuid();
        people.Known.Add(new Person { Id = existingPersonId, OwnerId = "owner", FirstName = "Ada" });
        await using var context = CreateContext(onboarding, new FakeUserTimeZoneService("UTC", Today), people: people);

        var cut = context.Render<OnboardingPage>();
        cut.Find("[data-testid='onboarding-timezone-save']").Click();

        cut.WaitForElement("[data-testid='onboarding-interaction-step']");
        cut.FindAll("[data-testid='onboarding-person-step']").Should().BeEmpty();
        people.ListPageQueries.Should().ContainSingle().Which.PageSize.Should().Be(1);
        onboarding.Dismissals.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_first_person_does_not_dismiss_onboarding()
    {
        var onboarding = new FakeOnboardingService();
        var people = new FakePeopleService
        {
            ThrowOnNextCreate = new PersonValidationException([PersonValidationError.FirstNameRequired]),
        };
        await using var context = CreateContext(onboarding, new FakeUserTimeZoneService("UTC", Today), people: people);

        var cut = context.Render<OnboardingPage>();
        cut.Find("[data-testid='onboarding-timezone-save']").Click();
        cut.WaitForElement("[data-testid='person-form']");
        cut.Find("[data-testid='person-form-save']").Click();

        cut.WaitForAssertion(() =>
        {
            people.Created.Should().BeEmpty();
            onboarding.Dismissals.Should().Be(0);
            cut.Markup.Should().Contain("Enter a first name.");
        });
    }

    [Fact]
    public async Task Interaction_validation_keeps_the_guide_pending_and_a_successful_retry_finishes_it()
    {
        var onboarding = new FakeOnboardingService();
        var people = new FakePeopleService();
        var personId = Guid.NewGuid();
        people.NextId = personId;
        var interactions = new FakeInteractionService
        {
            CandidateOptions = [new InteractionParticipantOption(personId, "Ada", false)],
            ThrowOnNextCreate = new InteractionValidationException([InteractionValidationError.DateInFuture]),
        };
        await using var context = CreateContext(
            onboarding,
            new FakeUserTimeZoneService("UTC", Today),
            people,
            interactions);

        var cut = context.Render<OnboardingPage>();
        cut.Find("[data-testid='onboarding-timezone-save']").Click();
        cut.WaitForElement("[data-testid='person-form']");
        cut.Find("[data-testid='person-first-name-field'] input").Input("Ada");
        cut.Find("[data-testid='person-form-save']").Click();
        cut.WaitForElement("[data-testid='interaction-editor']");
        cut.Find("[data-testid='interaction-description']").Change("Talked about the garden.");
        cut.Find("[data-testid='interaction-save']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='interaction-date-error']").TextContent.Should().NotBeNullOrWhiteSpace();
            interactions.Created.Should().BeEmpty();
            onboarding.Dismissals.Should().Be(0);
        });

        cut.Find("[data-testid='interaction-save']").Click();

        cut.WaitForAssertion(() =>
        {
            interactions.Created.Should().ContainSingle();
            onboarding.Dismissals.Should().Be(1);
            onboarding.IsPending.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Skipping_dismisses_and_returns_without_saving_a_step()
    {
        var onboarding = new FakeOnboardingService();
        var timeZone = new FakeUserTimeZoneService("UTC", Today);
        await using var context = CreateContext(onboarding, timeZone);

        var cut = context.Render<OnboardingPage>();
        cut.Find("[data-testid='onboarding-skip']").Click();

        cut.WaitForAssertion(() =>
        {
            onboarding.Dismissals.Should().Be(1);
            onboarding.IsPending.Should().BeFalse();
        });
        timeZone.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dismissed_account_cannot_open_the_guide_directly()
    {
        var onboarding = new FakeOnboardingService(isPending: false);
        await using var context = CreateContext(onboarding, new FakeUserTimeZoneService("UTC", Today));
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/onboarding");

        var cut = context.Render<OnboardingPage>();

        cut.WaitForAssertion(() => navigation.Uri.Should().Be("http://localhost/dashboard"));
        cut.FindAll("[data-testid='onboarding-timezone-step']").Should().BeEmpty();
        cut.FindAll("[data-testid='onboarding-skip']").Should().BeEmpty();
        onboarding.Dismissals.Should().Be(0);
    }

    private static BunitContext CreateContext(
        FakeOnboardingService onboarding,
        FakeUserTimeZoneService timeZone,
        FakePeopleService? people = null,
        FakeInteractionService? interactions = null,
        string? browserTimeZoneId = null)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddLogging();
        context.Services.AddSingleton<IOnboardingService>(onboarding);
        context.Services.AddSingleton<IUserTimeZoneService>(timeZone);
        context.Services.AddSingleton<IBrowserTimeZoneReader>(new FakeBrowserTimeZoneReader(browserTimeZoneId));
        context.Services.AddSingleton<IPeopleService>(people ?? new FakePeopleService());
        context.Services.AddSingleton<IRelationshipTypeService>(new FakeRelationshipTypeService());
        context.Services.AddSingleton<ITagService>(new FakeTagService());
        context.Services.AddSingleton<IInteractionService>(interactions ?? new FakeInteractionService());
        context.Render<MudPopoverProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }
}
