using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

/// <summary>
/// The person form's possible-duplicate check (issue #27): made when Save is pressed, shown instead
/// of saving, acknowledged with "Save anyway", and in edit mode only when the person is renamed.
/// The fake service stands in for the matching; what is asserted is when the form asks and how it
/// reacts.
/// </summary>
public class PersonFormDuplicateTests
{
    private static BunitContext CreateContext(FakePeopleService people, out IRenderedComponent<MudSnackbarProvider> snackbars)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        var types = new FakeRelationshipTypeService();
        types.Types.AddRange(RelationshipType.CreateDefaults("user"));
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IRelationshipTypeService>(types);
        context.Services.AddSingleton<ITagService>(new FakeTagService());
        context.Render<MudPopoverProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    private static PossibleDuplicate John(bool archived = false, params PossibleDuplicateReason[] reasons) =>
        new(Guid.NewGuid(), "John", "Smith", archived, reasons.Length == 0 ? [PossibleDuplicateReason.SimilarName] : reasons);

    private static FakePeopleService PeopleFinding(params PossibleDuplicate[] matches) =>
        new() { Duplicates = _ => matches };

    private static void Type(IRenderedComponent<PersonForm> cut, string field, string value) =>
        cut.Find($"[data-testid='{field}'] input, [data-testid='{field}'] textarea").Input(value);

    private static void Save(IRenderedComponent<PersonForm> cut) =>
        cut.Find("[data-testid='person-form-save']").Click();

    private static void SaveAnyway(IRenderedComponent<PersonForm> cut) =>
        cut.Find("[data-testid='person-duplicate-save-anyway']").Click();

    [Fact]
    public async Task Saving_a_new_person_checks_for_duplicates_and_shows_the_warning_instead_of_saving()
    {
        var john = John();
        var people = PeopleFinding(john);
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Jon");
        Type(cut, "person-last-name-field", "Smith");

        Save(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-duplicate-warning']").TextContent.Should().Contain(PossibleDuplicateText.Title));
        people.Created.Should().BeEmpty();
        var link = cut.Find("[data-testid='person-duplicate-link']");
        link.TextContent.Should().Contain("John Smith");
        link.GetAttribute("href").Should().Be($"/people/{john.Id}");
        link.GetAttribute("target").Should().Be("_blank");
        link.GetAttribute("rel").Should().Contain("noopener");
        link.TextContent.Should().Contain(PossibleDuplicateText.NewTabHint);
        cut.Find("[data-testid='person-duplicate-reason']").TextContent.Trim().Should().Be("Similar name");
    }

    [Fact]
    public async Task Save_anyway_saves_without_checking_again()
    {
        var people = PeopleFinding(John());
        await using var context = CreateContext(people, out var snackbars);
        Guid? saved = null;
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.OnSaved, (Guid id) => saved = id));
        Type(cut, "person-first-name-field", "Jon");
        Type(cut, "person-last-name-field", "Smith");
        Save(cut);
        cut.WaitForElement("[data-testid='person-duplicate-warning']");

        SaveAnyway(cut);

        cut.WaitForAssertion(() => people.Created.Should().ContainSingle());
        people.DuplicateQueries.Should().ContainSingle("the second attempt is the acknowledged one");
        cut.WaitForAssertion(() => saved.Should().Be(people.NextId));
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Person added"));
        cut.FindAll("[data-testid='person-duplicate-warning']").Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_the_name_after_the_warning_checks_again()
    {
        var people = PeopleFinding(John());
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Jon");
        Type(cut, "person-last-name-field", "Smith");
        Save(cut);
        cut.WaitForElement("[data-testid='person-duplicate-warning']");

        Type(cut, "person-first-name-field", "Jonn");
        Save(cut);

        cut.WaitForAssertion(() => people.DuplicateQueries.Should().HaveCount(2));
        people.DuplicateQueries.Last().FirstName.Should().Be("Jonn");
        people.Created.Should().BeEmpty("the changed names were checked again, not saved");
        cut.Find("[data-testid='person-duplicate-warning']").Should().NotBeNull();
    }

    [Fact]
    public async Task Pressing_Save_again_with_unchanged_names_checks_again_and_still_warns()
    {
        var people = PeopleFinding(John());
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Jon");
        Save(cut);
        cut.WaitForElement("[data-testid='person-duplicate-warning']");

        Save(cut);

        cut.WaitForAssertion(() => people.DuplicateQueries.Should().HaveCount(2));
        people.Created.Should().BeEmpty("only Save anyway acknowledges the warning");
    }

    [Fact]
    public async Task Saving_again_with_the_same_names_after_save_anyway_failed_validation_does_not_warn_again()
    {
        var people = PeopleFinding(John());
        people.ThrowOnNextCreate = new PersonValidationException([PersonValidationError.BirthdayInTheFuture]);
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Jon");
        Type(cut, "person-last-name-field", "Smith");
        Save(cut);
        cut.WaitForElement("[data-testid='person-duplicate-warning']");

        SaveAnyway(cut);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='person-duplicate-warning']").Should().BeEmpty());
        Save(cut);

        cut.WaitForAssertion(() => people.Created.Should().ContainSingle());
        people.DuplicateQueries.Should().ContainSingle("the acknowledgement survives a validation error");
    }

    [Fact]
    public async Task With_no_matches_the_person_is_saved_straight_away()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Ada");

        Save(cut);

        cut.WaitForAssertion(() => people.Created.Should().ContainSingle());
        people.DuplicateQueries.Should().ContainSingle();
        cut.FindAll("[data-testid='person-duplicate-warning']").Should().BeEmpty();
    }

    [Fact]
    public async Task A_blank_first_name_is_not_checked_and_shows_the_validation_message()
    {
        var people = PeopleFinding(John());
        people.ThrowOnNextCreate = new PersonValidationException([PersonValidationError.FirstNameRequired]);
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-last-name-field", "Smith");

        Save(cut);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Enter a first name."));
        people.DuplicateQueries.Should().BeEmpty();
        cut.FindAll("[data-testid='person-duplicate-warning']").Should().BeEmpty();
    }

    [Fact]
    public async Task The_query_carries_the_names_nickname_and_contact_methods_and_no_excluded_id()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Ada");
        Type(cut, "person-last-name-field", "Byron");
        Type(cut, "person-nickname-field", "Countess");
        cut.Find("[data-testid='person-contact-method-add']").Click();
        cut.Find("[data-testid='person-contact-method-value-field'] input, [data-testid='person-contact-method-value-field'] textarea")
            .Input("ada@example.com");

        Save(cut);

        cut.WaitForAssertion(() => people.DuplicateQueries.Should().ContainSingle());
        var query = people.DuplicateQueries.Single();
        query.FirstName.Should().Be("Ada");
        query.LastName.Should().Be("Byron");
        query.Nickname.Should().Be("Countess");
        query.ContactMethods.Should().ContainSingle().Which.Value.Should().Be("ada@example.com");
        query.ExcludePersonId.Should().BeNull();
    }

    [Fact]
    public async Task Archived_matches_are_labelled_archived()
    {
        var people = PeopleFinding(John(archived: true));
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "John");

        Save(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-duplicate-reason']").TextContent.Should().Contain("Archived"));
    }

    [Fact]
    public async Task Edit_mode_checks_only_when_the_name_changes_and_excludes_the_person()
    {
        var person = new Person { FirstName = "Ada", LastName = "Byron" };
        var people = PeopleFinding(John());
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));
        Type(cut, "person-first-name-field", "Jon");
        Type(cut, "person-last-name-field", "Smith");

        Save(cut);

        cut.WaitForElement("[data-testid='person-duplicate-warning']");
        people.Updated.Should().BeEmpty();
        people.DuplicateQueries.Should().ContainSingle().Which.ExcludePersonId.Should().Be(person.Id);

        SaveAnyway(cut);
        cut.WaitForAssertion(() => people.Updated.Should().ContainSingle());
    }

    [Fact]
    public async Task Edit_mode_with_only_a_casing_change_saves_without_checking()
    {
        var person = new Person { FirstName = "Ada", LastName = "Byron" };
        var people = PeopleFinding(John());
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));
        Type(cut, "person-first-name-field", "ADA");

        Save(cut);

        cut.WaitForAssertion(() => people.Updated.Should().ContainSingle());
        people.DuplicateQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_mode_with_a_new_email_but_the_same_name_saves_without_checking()
    {
        var person = new Person { FirstName = "Ada", LastName = "Byron" };
        var people = PeopleFinding(John());
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));
        cut.Find("[data-testid='person-contact-method-add']").Click();
        cut.Find("[data-testid='person-contact-method-value-field'] input, [data-testid='person-contact-method-value-field'] textarea")
            .Input("new@example.com");

        Save(cut);

        cut.WaitForAssertion(() => people.Updated.Should().ContainSingle());
        people.DuplicateQueries.Should().BeEmpty("only a rename is checked in edit mode");
    }

    [Fact]
    public async Task In_edit_mode_each_match_offers_Merge_instead_linking_to_the_merge_page()
    {
        var person = new Person { FirstName = "Ada", LastName = "Byron" };
        var john = John();
        var grace = new PossibleDuplicate(Guid.NewGuid(), "Grace", "Hopper", false, [PossibleDuplicateReason.SameEmail]);
        var people = PeopleFinding(john, grace);
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));
        Type(cut, "person-first-name-field", "Jon");
        Type(cut, "person-last-name-field", "Smith");

        Save(cut);

        cut.WaitForElement("[data-testid='person-duplicate-warning']");
        var links = cut.FindAll("[data-testid='person-duplicate-merge']");
        links.Select(link => link.GetAttribute("href")).Should().Equal(
            $"/people/{person.Id}/merge?with={john.Id}",
            $"/people/{person.Id}/merge?with={grace.Id}");
        links.Should().OnlyContain(link => link.TextContent.Trim() == "Merge instead");
        links.Should().OnlyContain(link => link.GetAttribute("target") == null, "it opens in the same tab: merging instead of saving is the point");
    }

    [Fact]
    public async Task In_create_mode_no_merge_link_is_shown()
    {
        var people = PeopleFinding(John());
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Jon");
        Type(cut, "person-last-name-field", "Smith");

        Save(cut);

        cut.WaitForElement("[data-testid='person-duplicate-warning']");
        cut.FindAll("[data-testid='person-duplicate-merge']").Should().BeEmpty("a person being created has no profile to merge into");
        cut.FindAll("[data-testid='person-duplicate-link']").Should().ContainSingle();
    }

    [Fact]
    public async Task The_warning_sits_directly_above_the_form_actions()
    {
        var people = PeopleFinding(John());
        await using var context = CreateContext(people, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Jon");
        Save(cut);

        cut.WaitForElement("[data-testid='person-duplicate-warning']");

        var form = cut.Find("[data-testid='person-form']");
        var children = form.Children.ToList();
        var warning = children.FindIndex(c => c.GetAttribute("data-testid") == "person-duplicate-warning");
        var actions = children.FindIndex(c => c.ClassList.Contains("rl-form-actions"));
        warning.Should().BeGreaterThanOrEqualTo(0);
        warning.Should().Be(actions - 1, "the warning is directly above the Save button");
        cut.Find("[data-testid='person-duplicate-save-anyway']").HasAttribute("disabled").Should().BeFalse();
    }
}
