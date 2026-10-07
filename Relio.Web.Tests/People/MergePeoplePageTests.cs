using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.People;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Components.Pages;
using Relio.Web.Components.People;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

/// <summary>The merge page (issue #28): choosing the other profile, choosing what to keep, confirming.</summary>
public class MergePeoplePageTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private sealed record Harness(
        FakePeopleService People,
        FakePersonMergeService Merge,
        IRenderedComponent<MudPopoverProvider> Popovers,
        IRenderedComponent<MudDialogProvider> Dialogs,
        IRenderedComponent<MudSnackbarProvider> Snackbars);

    private static BunitContext CreateContext(out Harness harness)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        var people = new FakePeopleService();
        var merge = new FakePersonMergeService(people);
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IPersonMergeService>(merge);
        context.Services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService("UTC", Today));
        harness = new Harness(
            people,
            merge,
            context.Render<MudPopoverProvider>(),
            context.Render<MudDialogProvider>(),
            context.Render<MudSnackbarProvider>());
        return context;
    }

    /// <summary>Renders the page at <c>/people/{id}/merge</c> plus <c>?with=</c>, as the router would.</summary>
    private static IRenderedComponent<MergePeople> RenderAt(BunitContext context, Guid personId, string? with = null)
    {
        var query = with is null ? string.Empty : $"?with={with}";
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/people/{personId}/merge{query}");
        return context.Render<MergePeople>(parameters => parameters.Add(p => p.PersonId, personId));
    }

    private static Person John() => new()
    {
        FirstName = "John",
        LastName = "Smith",
        Nickname = "Johnny",
        Details = "Plays chess.",
    };

    private static Person Jon() => new()
    {
        FirstName = "Jon",
        LastName = "Smythe",
        Nickname = "Jonny",
        Details = "Climbs on Tuesdays.",
    };

    private static ContactMethod Contact(Person person, ContactMethodKind kind, string value, int order, string? label = null)
    {
        var method = new ContactMethod
        {
            PersonId = person.Id,
            Kind = kind,
            Label = label,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(kind, value),
            SortOrder = order,
        };
        person.ContactMethods.Add(method);
        return method;
    }

    private static (Person Primary, Person Other) SeedPair(Harness harness)
    {
        var primary = John();
        var other = Jon();
        harness.People.Known.AddRange([primary, other]);
        return (primary, other);
    }

    private static IElement Field(IRenderedComponent<MergePeople> cut, MergeField field) =>
        cut.Find($"[data-testid='merge-field'][data-field='{field}']");

    private static string[] CheckedChoices(IElement field) =>
        field.QuerySelectorAll("input[data-testid^='merge-choice-']")
            .Where(input => input.HasAttribute("checked"))
            .Select(input => input.GetAttribute("data-testid")!)
            .ToArray();

    private static void Choose(IElement field, string testId) =>
        field.QuerySelector($"input[data-testid='{testId}']")!.Click();

    // ---- Not found and step 1 ---------------------------------------------------------------

    [Fact]
    public async Task Shows_not_found_when_the_person_is_missing()
    {
        await using var context = CreateContext(out _);

        var cut = RenderAt(context, Guid.NewGuid());

        cut.Find("[data-testid='person-not-found']").TextContent.Should().Contain("This person isn't in your list");
        cut.FindAll("[data-testid='merge-back']").Should().BeEmpty();
        cut.FindAll("[data-testid='merge-submit']").Should().BeEmpty();
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task Offers_suggestions_and_a_picker_but_never_the_person_themselves()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var grace = new Person { FirstName = "Grace" };
        harness.People.Known.Add(grace);
        harness.Merge.Candidates = new MergeCandidates(
            [new PossibleDuplicate(other.Id, other.FirstName, other.LastName, false, [PossibleDuplicateReason.SameEmail])],
            [new PersonSummary(grace.Id, "Grace", null, false), new PersonSummary(other.Id, other.FirstName, other.LastName, false)]);

        var cut = RenderAt(context, primary.Id);

        cut.Find("h1").TextContent.Should().Be("Merge profiles");
        cut.Find("[data-testid='merge-back']").GetAttribute("href").Should().Be($"/people/{primary.Id}");
        var suggestions = cut.Find("[data-testid='merge-suggestions']");
        suggestions.QuerySelectorAll("[data-testid='merge-suggestion']").Should().ContainSingle().Which.TextContent.Trim().Should().Be("Jon Smythe");
        suggestions.TextContent.Should().Contain("Same email address").And.NotContain("John Smith");
        cut.Find("[data-testid='merge-picker-field'] input").Should().NotBeNull();
        cut.Find("[data-testid='merge-primary-name']").TextContent.Should().Be("John Smith");
        harness.Merge.CandidateCalls.Should().Equal(primary.Id);
        cut.FindAll("[data-testid='merge-submit']").Should().BeEmpty("nothing to merge until the other profile is chosen");
    }

    [Fact]
    public async Task The_picker_offers_every_other_person_and_filters_as_you_type()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var grace = new Person { FirstName = "Grace", LastName = "Hopper" };
        harness.People.Known.Add(grace);
        harness.Merge.Candidates = new MergeCandidates(
            [],
            [
                new PersonSummary(grace.Id, "Grace", "Hopper", true),
                new PersonSummary(other.Id, other.FirstName, other.LastName, false),
            ]);
        var cut = RenderAt(context, primary.Id);

        cut.Find("[data-testid='merge-picker-field'] input").Input("hop");
        var options = harness.Popovers.WaitForElements(".mud-popover-open .mud-list-item");
        options.Select(o => o.TextContent.Trim()).Should().Equal("Grace Hopper · Archived");
        options.Single().Click();

        // The choice is handled asynchronously, so wait for it.
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => navigation.Uri.Should().EndWith($"/people/{primary.Id}/merge?with={grace.Id}"));
    }

    [Fact]
    public async Task Choosing_a_suggestion_puts_its_id_in_the_url_as_a_new_history_entry()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        harness.Merge.Candidates = new MergeCandidates(
            [new PossibleDuplicate(other.Id, other.FirstName, other.LastName, false, [PossibleDuplicateReason.SimilarName])],
            [new PersonSummary(other.Id, other.FirstName, other.LastName, false)]);
        var cut = RenderAt(context, primary.Id);

        cut.Find("[data-testid='merge-suggestion']").Click();

        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.Uri.Should().EndWith($"/people/{primary.Id}/merge?with={other.Id}");
        ((BunitNavigationManager)navigation).History.First().Options.ReplaceHistoryEntry.Should().BeFalse("Back returns to choosing");
        // The address is opaque ids only.
        navigation.Uri.Should().NotContain("Jon").And.NotContain("Smith");
    }

    [Fact]
    public async Task An_unknown_with_id_shows_a_calm_message_and_the_picker()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        harness.Merge.Candidates = new MergeCandidates([], [new PersonSummary(other.Id, "Jon", "Smythe", false)]);

        var cut = RenderAt(context, primary.Id, Guid.NewGuid().ToString());

        cut.Find("[data-testid='merge-other-missing']").TextContent.Should().Contain("That profile isn't in your list");
        cut.Find("[data-testid='merge-picker-field']").Should().NotBeNull();
        cut.FindAll("[data-testid='merge-submit']").Should().BeEmpty();
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task A_with_that_is_not_an_id_or_is_the_person_themselves_is_ignored()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        harness.Merge.Candidates = new MergeCandidates([], [new PersonSummary(other.Id, "Jon", "Smythe", false)]);

        var garbage = RenderAt(context, primary.Id, "not-an-id");
        garbage.FindAll("[data-testid='merge-other-missing']").Should().BeEmpty();
        garbage.FindAll("[data-testid='merge-picker-field']").Should().ContainSingle();

        var itself = RenderAt(context, primary.Id, primary.Id.ToString());
        itself.FindAll("[data-testid='merge-heads']").Should().BeEmpty("a profile cannot be merged with itself");
        itself.FindAll("[data-testid='merge-picker-field']").Should().ContainSingle();
    }

    [Fact]
    public async Task With_no_other_people_shows_the_empty_state_with_an_h2()
    {
        await using var context = CreateContext(out var harness);
        var primary = John();
        harness.People.Known.Add(primary);

        var cut = RenderAt(context, primary.Id);

        cut.Find("[data-testid='merge-no-candidates']").TextContent.Should().Contain("No other profiles yet");
        cut.Find("[data-testid='merge-no-candidates'] h2").TextContent.Should().Be("No other profiles yet");
        cut.FindAll("h1").Should().ContainSingle().Which.TextContent.Should().Be("Merge profiles");
        cut.FindAll("[data-testid='merge-picker-field']").Should().BeEmpty();

        cut.Find("[data-testid='merge-no-candidates'] button").Click();
        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/people/{primary.Id}");
    }

    // ---- Step 2 -----------------------------------------------------------------------------

    [Fact]
    public async Task Only_conflicting_fields_get_a_choice()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        primary.HowWeMet = "Conference";

        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        cut.FindAll("[data-testid='merge-field']").Select(f => f.GetAttribute("data-field")).Should()
            .Equal("Name", "Nickname", "Details");
        cut.FindAll("[data-testid='merge-no-conflicts']").Should().BeEmpty();
        Field(cut, MergeField.Name).QuerySelector("legend")!.TextContent.Should().Be("Name");
        Field(cut, MergeField.Name).TextContent.Should().Contain("John Smith").And.Contain("Jon Smythe");
        cut.Find("[data-testid='merge-heads']").TextContent.Should().Contain("John Smith").And.Contain("Jon Smythe");
    }

    [Fact]
    public async Task Without_conflicts_the_page_says_the_details_will_be_combined()
    {
        await using var context = CreateContext(out var harness);
        var primary = new Person { FirstName = "Ada", LastName = "Lovelace" };
        var other = new Person { FirstName = "Ada", LastName = "Lovelace", Nickname = "Countess" };
        harness.People.Known.AddRange([primary, other]);

        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        cut.Find("[data-testid='merge-no-conflicts']").TextContent.Should().Contain("don't disagree");
        cut.FindAll("[data-testid='merge-field']").Should().BeEmpty();
        cut.Find("[data-testid='merge-preview-nickname']").TextContent.Should().Be("Countess");
        cut.Find("[data-testid='merge-submit']").Should().NotBeNull();
    }

    [Fact]
    public async Task Choices_default_to_this_profile_and_to_the_active_status()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        primary.IsArchived = true;
        primary.ArchivedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        CheckedChoices(Field(cut, MergeField.Name)).Should().Equal("merge-choice-primary");
        CheckedChoices(Field(cut, MergeField.Details)).Should().Equal("merge-choice-primary");
        CheckedChoices(Field(cut, MergeField.ArchivedState)).Should().Equal(["merge-choice-duplicate"], "the active profile is kept by default");
        cut.Find("[data-testid='merge-preview-status']").TextContent.Should().Be("Active");
    }

    [Fact]
    public async Task Keep_both_is_offered_only_for_text_fields_and_is_disabled_when_too_long()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        primary.HowWeMet = "Conference";
        other.HowWeMet = new string('b', Person.HowWeMetMaxLength);

        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        Field(cut, MergeField.Details).QuerySelector("input[data-testid='merge-choice-both']").Should().NotBeNull();
        Field(cut, MergeField.Details).QuerySelector("input[data-testid='merge-choice-both']")!.HasAttribute("disabled").Should().BeFalse();
        Field(cut, MergeField.HowWeMet).QuerySelector("input[data-testid='merge-choice-both']")!.HasAttribute("disabled").Should().BeTrue("the two texts together would be too long");
        Field(cut, MergeField.Name).QuerySelector("input[data-testid='merge-choice-both']").Should().BeNull();
        Field(cut, MergeField.Nickname).QuerySelector("input[data-testid='merge-choice-both']").Should().BeNull();
    }

    [Fact]
    public async Task The_preview_shows_the_union_of_contact_methods_and_tags_without_repeats()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        Contact(primary, ContactMethodKind.Email, "john@example.com", 0);
        Contact(other, ContactMethodKind.Email, "JOHN@example.com", 0, "Work");
        Contact(other, ContactMethodKind.Phone, "+44 7700 900123", 1);
        var chess = new Tag { Name = "Chess" };
        var climbing = new Tag { Name = "Climbing" };
        primary.Tags.Add(chess);
        other.Tags.Add(chess);
        other.Tags.Add(climbing);

        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        var methods = cut.FindAll("[data-testid='merge-preview-contact-method']");
        methods.Should().HaveCount(2);
        methods[0].TextContent.Should().Contain("Email · Work").And.Contain("john@example.com", "the primary's spelling stays, with the label of the repeat");
        methods[1].TextContent.Should().Contain("Phone").And.Contain("+44 7700 900123");
        cut.Find("[data-testid='merge-preview-repeated']").TextContent.Should().Be("1 repeated detail combined");
        cut.FindAll("[data-testid='merge-preview-tag']").Select(t => t.TextContent.Trim()).Should().Equal("Chess", "Climbing");
    }

    [Fact]
    public async Task The_preview_names_the_relationship_only_when_there_is_one()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);

        var without = RenderAt(context, primary.Id, other.Id.ToString());
        without.FindAll("[data-testid='merge-preview-relationship']").Should().BeEmpty();

        var friend = new RelationshipType { Name = "Friend" };
        other.RelationshipType = friend;
        other.RelationshipTypeId = friend.Id;
        var with = RenderAt(context, primary.Id, other.Id.ToString());

        with.Find("[data-testid='merge-preview-relationship']").TextContent.Should().Be("Friend");
        with.FindAll("[data-testid='merge-field'][data-field='RelationshipType']").Should().BeEmpty("only one side has a type, so it is not a conflict");
    }

    [Fact]
    public async Task The_preview_shows_the_later_last_contacted_date_in_words()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        primary.LastContactedOn = Today.AddDays(-45);
        other.LastContactedOn = Today.AddDays(-3);

        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        cut.Find("[data-testid='merge-preview-last-contacted']").TextContent.Should().Be("3 days ago");
    }

    [Fact]
    public async Task Changing_a_choice_updates_the_preview()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());
        cut.Find("[data-testid='merge-preview-name']").TextContent.Should().Be("John Smith");
        cut.Find("[data-testid='merge-preview-details']").TextContent.Should().Be("Plays chess.");

        Choose(Field(cut, MergeField.Name), "merge-choice-duplicate");
        Choose(Field(cut, MergeField.Details), "merge-choice-both");

        cut.WaitForAssertion(() => cut.Find("[data-testid='merge-preview-name']").TextContent.Should().Be("Jon Smythe"));
        cut.Find("[data-testid='merge-preview-details']").TextContent.Should().Be("Plays chess.\n\nClimbs on Tuesdays.");
        CheckedChoices(Field(cut, MergeField.Name)).Should().Equal("merge-choice-duplicate");
    }

    [Fact]
    public async Task User_text_is_set_in_the_serif_and_rendered_as_text()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        other.Details = "<b>bold</b> & more";

        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        var details = Field(cut, MergeField.Details);
        details.QuerySelectorAll(".rl-entry-text").Should().HaveCount(2);
        details.QuerySelectorAll(".rl-multiline").Should().HaveCount(2);
        details.QuerySelector("b").Should().BeNull("text is never markup");
        details.TextContent.Should().Contain("<b>bold</b> & more");
    }

    [Fact]
    public async Task The_swap_link_points_to_the_other_profiles_merge_page()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);

        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        cut.Find("[data-testid='merge-swap']").GetAttribute("href").Should().Be($"/people/{other.Id}/merge?with={primary.Id}");
        cut.Find("[data-testid='merge-cancel']").GetAttribute("href").Should().Be($"/people/{primary.Id}");
        cut.Find("[data-testid='merge-primary-open']").GetAttribute("target").Should().Be("_blank");
        cut.Find("[data-testid='merge-primary-open']").GetAttribute("rel").Should().Contain("noopener");
    }

    [Fact]
    public async Task Choosing_a_different_profile_goes_back_to_step_one()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        cut.Find("[data-testid='merge-change']").Click();

        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/people/{primary.Id}/merge");
    }

    // ---- Merging ----------------------------------------------------------------------------

    [Fact]
    public async Task Merging_asks_for_confirmation_naming_both_people_with_a_destructive_button()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        cut.Find("[data-testid='merge-submit']").Click();

        harness.Dialogs.WaitForAssertion(() => harness.Dialogs.Markup.Should().Contain("Merge Jon Smythe into John Smith?"));
        harness.Dialogs.Markup.Should().Contain("Jon Smythe's profile will be removed.").And.Contain("This can't be undone.");
        var confirm = harness.Dialogs.Find("[data-testid='confirm-dialog-confirm']");
        confirm.TextContent.Trim().Should().Be("Merge profiles");
        confirm.ClassList.Should().Contain("mud-button-filled-error");
        harness.Merge.Merges.Should().BeEmpty("nothing is merged before the confirmation");
    }

    [Fact]
    public async Task Cancelling_the_confirmation_merges_nothing()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var uriBefore = navigation.Uri;
        cut.Find("[data-testid='merge-submit']").Click();
        harness.Dialogs.WaitForAssertion(() => harness.Dialogs.FindAll("[data-testid='confirm-dialog-cancel']").Should().ContainSingle());

        harness.Dialogs.Find("[data-testid='confirm-dialog-cancel']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='merge-submit']").HasAttribute("disabled").Should().BeFalse());
        harness.Merge.Merges.Should().BeEmpty();
        navigation.Uri.Should().Be(uriBefore);
        harness.Snackbars.Markup.Should().NotContain("Profiles merged");
    }

    [Fact]
    public async Task Confirming_sends_the_choices_says_Profiles_merged_and_replaces_history_with_the_profile()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());
        Choose(Field(cut, MergeField.Nickname), "merge-choice-duplicate");
        Choose(Field(cut, MergeField.Details), "merge-choice-both");
        cut.Find("[data-testid='merge-submit']").Click();
        harness.Dialogs.WaitForAssertion(() => harness.Dialogs.FindAll("[data-testid='confirm-dialog-confirm']").Should().ContainSingle());

        harness.Dialogs.Find("[data-testid='confirm-dialog-confirm']").Click();

        var navigation = context.Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => harness.Merge.Merges.Should().ContainSingle());
        var request = harness.Merge.Merges.Single();
        request.PrimaryId.Should().Be(primary.Id);
        request.DuplicateId.Should().Be(other.Id);
        request.FieldChoices.Should().BeEquivalentTo(new Dictionary<MergeField, MergeFieldChoice>
        {
            [MergeField.Name] = MergeFieldChoice.Primary,
            [MergeField.Nickname] = MergeFieldChoice.Duplicate,
            [MergeField.Details] = MergeFieldChoice.Both,
        });
        cut.WaitForAssertion(() => navigation.Uri.Should().EndWith($"/people/{primary.Id}"));
        var entry = ((BunitNavigationManager)navigation).History.First();
        entry.Options.ReplaceHistoryEntry.Should().BeTrue("Back must not land on the merge page of a profile that is gone");
        harness.Snackbars.WaitForAssertion(() => harness.Snackbars.Markup.Should().Contain("Profiles merged"));
        harness.Snackbars.Markup.Should().NotContain("John").And.NotContain("Jon");
    }

    [Fact]
    public async Task A_profile_removed_meanwhile_shows_a_problem_and_reloads()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        harness.Merge.Outcome = MergeOutcome.NotFound;
        harness.Merge.Candidates = new MergeCandidates([], [new PersonSummary(Guid.NewGuid(), "Grace", null, false)]);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());
        cut.Find("[data-testid='merge-submit']").Click();
        harness.Dialogs.WaitForAssertion(() => harness.Dialogs.FindAll("[data-testid='confirm-dialog-confirm']").Should().ContainSingle());
        harness.People.Known.Remove(other); // removed in another tab while the dialog was open

        harness.Dialogs.Find("[data-testid='confirm-dialog-confirm']").Click();

        harness.Snackbars.WaitForAssertion(() => harness.Snackbars.Markup.Should().Contain("One of these profiles is no longer in your list."));
        cut.WaitForAssertion(() => cut.Find("[data-testid='merge-other-missing']").Should().NotBeNull());
        cut.FindAll("[data-testid='merge-submit']").Should().BeEmpty();
        context.Services.GetRequiredService<NavigationManager>().Uri.Should().Contain("/merge?with=", "the address is left as it was");
    }

    [Fact]
    public async Task A_limit_problem_is_explained_and_nothing_navigates()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        harness.Merge.ThrowOnNextMerge = new PersonValidationException(
            [PersonValidationError.TooManyContactMethods, PersonValidationError.TooManyTags, PersonValidationError.DetailsTooLong]);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var uriBefore = navigation.Uri;
        cut.Find("[data-testid='merge-submit']").Click();
        harness.Dialogs.WaitForAssertion(() => harness.Dialogs.FindAll("[data-testid='confirm-dialog-confirm']").Should().ContainSingle());

        harness.Dialogs.Find("[data-testid='confirm-dialog-confirm']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='merge-problem']").Should().NotBeNull());
        var problems = cut.Find("[data-testid='merge-problem']").TextContent;
        problems.Should().Contain("more than 20 contact methods").And.Contain("more than 20 tags").And.Contain("Both texts together are too long");
        navigation.Uri.Should().Be(uriBefore);
        harness.Snackbars.Markup.Should().NotContain("Profiles merged");
        cut.Find("[data-testid='merge-submit']").HasAttribute("disabled").Should().BeFalse("the user can fix a choice and try again");
    }

    [Fact]
    public async Task A_second_click_while_merging_does_not_start_a_second_merge()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());

        cut.Find("[data-testid='merge-submit']").Click();
        harness.Dialogs.WaitForAssertion(() => harness.Dialogs.FindAll("[data-testid='confirm-dialog-confirm']").Should().ContainSingle());

        cut.Find("[data-testid='merge-submit']").HasAttribute("disabled").Should().BeTrue("the button is disabled while the dialog is open");
        harness.Merge.Merges.Should().BeEmpty();
    }

    // ---- Page rules -------------------------------------------------------------------------

    [Fact]
    public async Task Every_state_has_exactly_one_h1()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        harness.Merge.Candidates = new MergeCandidates([], [new PersonSummary(other.Id, "Jon", "Smythe", false)]);

        RenderAt(context, Guid.NewGuid()).FindAll("h1").Should().ContainSingle();
        RenderAt(context, primary.Id).FindAll("h1").Should().ContainSingle();
        RenderAt(context, primary.Id, Guid.NewGuid().ToString()).FindAll("h1").Should().ContainSingle();
        RenderAt(context, primary.Id, other.Id.ToString()).FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task The_page_title_is_generic()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var head = context.Render<HeadOutlet>();

        RenderAt(context, primary.Id, other.Id.ToString());

        head.WaitForAssertion(() => head.Markup.Should().Contain("<title>Merge profiles - Relio</title>"));
        head.Markup.Should().NotContain("John").And.NotContain("Jon").And.NotContain("Smith");
    }

    [Fact]
    public async Task Loads_again_when_the_route_changes_to_the_other_profile()
    {
        await using var context = CreateContext(out var harness);
        var (primary, other) = SeedPair(harness);
        var cut = RenderAt(context, primary.Id, other.Id.ToString());
        cut.Find("[data-testid='merge-primary-name']").TextContent.Should().Be("John Smith");

        // The swap link: the other profile is now the one that is kept.
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/people/{other.Id}/merge?with={primary.Id}");
        cut.Render(parameters => parameters.Add(p => p.PersonId, other.Id));

        cut.WaitForAssertion(() => cut.Find("[data-testid='merge-primary-name']").TextContent.Should().Be("Jon Smythe"));
        cut.Find("[data-testid='merge-other-name']").TextContent.Should().Be("John Smith");
    }
}
