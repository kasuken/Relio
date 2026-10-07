using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

public class PersonFormTests
{
    // See ConfirmDialogTests for why the context is created per test with "await using".
    private static BunitContext CreateContext(
        FakePeopleService people,
        FakeRelationshipTypeService types,
        out IRenderedComponent<MudSnackbarProvider> snackbars,
        out IRenderedComponent<MudPopoverProvider> popovers,
        FakeTagService? tags = null)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IRelationshipTypeService>(types);
        context.Services.AddSingleton<ITagService>(tags ?? new FakeTagService());
        popovers = context.Render<MudPopoverProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    private static FakeRelationshipTypeService DefaultTypes()
    {
        var types = new FakeRelationshipTypeService();
        types.Types.AddRange(RelationshipType.CreateDefaults("user"));
        return types;
    }

    private static void Type(IRenderedComponent<PersonForm> cut, string field, string value) =>
        cut.Find($"[data-testid='{field}'] input, [data-testid='{field}'] textarea").Input(value);

    private static void Save(IRenderedComponent<PersonForm> cut) =>
        cut.Find("[data-testid='person-form-save']").Click();

    [Fact]
    public async Task Saving_sends_the_entered_values_to_the_service()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, DefaultTypes(), out _, out _);
        var cut = context.Render<PersonForm>();

        Type(cut, "person-first-name-field", "Ada");
        Type(cut, "person-last-name-field", "Lovelace");
        Type(cut, "person-nickname-field", "Countess");
        Type(cut, "person-birthday-day-field", "10");
        Type(cut, "person-birthday-year-field", "1815");
        Type(cut, "person-how-we-met-field", "At a talk.");
        Type(cut, "person-details-field", "Writes letters.");
        Save(cut);

        cut.WaitForAssertion(() => people.Created.Should().ContainSingle());
        var request = people.Created.Single();
        request.FirstName.Should().Be("Ada");
        request.LastName.Should().Be("Lovelace");
        request.Nickname.Should().Be("Countess");
        request.BirthdayDay.Should().Be(10);
        request.BirthdayYear.Should().Be(1815);
        request.BirthdayMonth.Should().BeNull("the month was not chosen");
        request.HowWeMet.Should().Be("At a talk.");
        request.Details.Should().Be("Writes letters.");
        request.RelationshipTypeId.Should().BeNull();
    }

    [Fact]
    public async Task Lists_the_relationship_types_and_sends_the_chosen_one()
    {
        var people = new FakePeopleService();
        var types = DefaultTypes();
        await using var context = CreateContext(people, types, out _, out var popovers);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Ada");

        cut.Find("[data-testid='person-relationship-field'] .mud-input-control").MouseDown();
        var items = popovers.WaitForElements(".mud-list-item");
        items.Select(i => i.TextContent.Trim()).Should().Equal(
            "Family", "Partner", "Friend", "Colleague", "Acquaintance", "Other");
        items.Single(i => i.TextContent.Trim() == "Friend").Click();
        Save(cut);

        cut.WaitForAssertion(() => people.Created.Should().ContainSingle());
        people.Created.Single().RelationshipTypeId.Should().Be(types.Types.Single(t => t.Name == "Friend").Id);
    }

    [Fact]
    public async Task With_no_relationship_types_the_select_is_disabled_and_explains_where_to_add_them()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, new FakeRelationshipTypeService(), out _, out _);

        var cut = context.Render<PersonForm>();

        var field = cut.Find("[data-testid='person-relationship-field']");
        field.TextContent.Should().Contain("You have no relationship types. Add them in Settings.");
        field.QuerySelector("input")!.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task With_relationship_types_the_select_is_enabled_without_the_hint()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, DefaultTypes(), out _, out _);

        var cut = context.Render<PersonForm>();

        var field = cut.Find("[data-testid='person-relationship-field']");
        field.TextContent.Should().NotContain("no relationship types");
        field.QuerySelector("input")!.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task Offers_the_twelve_months_by_name_and_sends_the_chosen_one_as_a_number()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, DefaultTypes(), out _, out var popovers);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Ada");

        cut.Find("[data-testid='person-birthday-month-field'] .mud-input-control").MouseDown();
        var items = popovers.WaitForElements(".mud-list-item");
        items.Select(i => i.TextContent.Trim()).Should().Equal(
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December");
        items.Single(i => i.TextContent.Trim() == "February").Click();
        Save(cut);

        cut.WaitForAssertion(() => people.Created.Should().ContainSingle());
        people.Created.Single().BirthdayMonth.Should().Be(2);
    }

    [Fact]
    public async Task A_successful_save_raises_OnSaved_with_the_new_id_and_shows_Person_added()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, DefaultTypes(), out var snackbars, out _);
        Guid? saved = null;
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.OnSaved, (Guid id) => saved = id));
        Type(cut, "person-first-name-field", "Ada");

        Save(cut);

        cut.WaitForAssertion(() => saved.Should().Be(people.NextId));
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Person added"));
    }

    [Fact]
    public async Task A_blank_first_name_shows_Enter_a_first_name()
    {
        // The service is the authority: it reports FirstNameRequired and the form words it.
        var people = new FakePeopleService
        {
            ThrowOnNextCreate = new PersonValidationException([PersonValidationError.FirstNameRequired]),
        };
        await using var context = CreateContext(people, DefaultTypes(), out var snackbars, out _);
        Guid? saved = null;
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.OnSaved, (Guid id) => saved = id));

        Save(cut);

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='person-first-name-field']").TextContent.Should().Contain("Enter a first name."));
        saved.Should().BeNull();
        snackbars.Markup.Should().NotContain("Person added");
    }

    [Fact]
    public async Task Text_that_is_too_long_is_explained_under_its_own_field()
    {
        var people = new FakePeopleService
        {
            ThrowOnNextCreate = new PersonValidationException(
                [PersonValidationError.NicknameTooLong, PersonValidationError.DetailsTooLong]),
        };
        await using var context = CreateContext(people, DefaultTypes(), out _, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Ada");

        Save(cut);

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='person-nickname-field']").TextContent
                .Should().Contain("Keep the nickname to 100 characters or fewer.");
            cut.Find("[data-testid='person-details-field']").TextContent
                .Should().Contain("Keep the details to 4,000 characters or fewer.");
        });
        cut.Find("[data-testid='person-first-name-field']").TextContent.Should().NotContain("Enter a first name.");
    }

    [Theory]
    [InlineData(PersonValidationError.BirthdayIncomplete, "Choose both a day and a month, or leave the birthday empty.")]
    [InlineData(PersonValidationError.BirthdayYearWithoutDayAndMonth, "Add the day and month too, or clear the year.")]
    [InlineData(PersonValidationError.BirthdayNotARealDate, "That date doesn't exist. Check the day, month and year.")]
    [InlineData(PersonValidationError.BirthdayInTheFuture, "Enter a date in the past or today.")]
    public async Task Birthday_errors_are_shown_under_the_birthday_fields(PersonValidationError error, string message)
    {
        var people = new FakePeopleService { ThrowOnNextCreate = new PersonValidationException([error]) };
        await using var context = CreateContext(people, DefaultTypes(), out _, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Ada");

        Save(cut);

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='person-birthday-error']").TextContent.Trim().Should().Be(message));
        cut.Find("[data-testid='person-birthday']").QuerySelector("[data-testid='person-birthday-error']")
            .Should().NotBeNull("the error sits inside the birthday group");
    }

    [Fact]
    public async Task A_second_save_clears_the_errors_of_the_first()
    {
        var people = new FakePeopleService
        {
            ThrowOnNextCreate = new PersonValidationException([PersonValidationError.FirstNameRequired]),
        };
        await using var context = CreateContext(people, DefaultTypes(), out _, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.OnSaved, (Guid _) => { }));
        Save(cut);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Enter a first name."));

        Type(cut, "person-first-name-field", "Ada");
        Save(cut);

        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Enter a first name."));
        people.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unavailable_relationship_type_shows_a_problem_and_reloads_the_choices()
    {
        var people = new FakePeopleService
        {
            ThrowOnNextCreate = new ForeignEntityNotOwnedException(ForeignEntityNames.RelationshipTypes),
        };
        var types = DefaultTypes();
        await using var context = CreateContext(people, types, out var snackbars, out _);
        var cut = context.Render<PersonForm>();
        Type(cut, "person-first-name-field", "Ada");
        var callsBefore = types.ListCalls;

        Save(cut);

        snackbars.WaitForAssertion(() => snackbars.Markup.Should()
            .Contain("That relationship type is no longer available. Choose another one."));
        types.ListCalls.Should().BeGreaterThan(callsBefore);
        people.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Has_a_cancel_link_back_to_the_people_list()
    {
        await using var context = CreateContext(new FakePeopleService(), DefaultTypes(), out _, out _);

        var cut = context.Render<PersonForm>();

        cut.Find("[data-testid='person-form-cancel']").GetAttribute("href").Should().Be("/people");
    }

    [Fact]
    public async Task Only_the_first_name_is_marked_required()
    {
        await using var context = CreateContext(new FakePeopleService(), DefaultTypes(), out _, out _);

        var cut = context.Render<PersonForm>();

        // The browser's own "Please fill out this field" bubble must not pre-empt Relio's message.
        cut.Find("[data-testid='person-form']").HasAttribute("novalidate").Should().BeTrue();
        cut.Find("[data-testid='person-first-name-field'] input").HasAttribute("required").Should().BeTrue();
        cut.Find("[data-testid='person-last-name-field'] input").HasAttribute("required").Should().BeFalse();
        cut.Find("[data-testid='person-first-name-field']").TextContent
            .Should().Contain("The only thing Relio needs.");
    }

    [Fact]
    public async Task Birthday_reminder_disabled_and_lead_days_can_be_configured_in_form()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, DefaultTypes(), out _, out _);
        var cut = context.Render<PersonForm>();

        cut.Find("[data-testid='person-birthday-reminder-disabled']").Should().NotBeNull();
        cut.Find("[data-testid='person-birthday-lead-days']").Should().NotBeNull();

        Type(cut, "person-first-name-field", "Grace");
        cut.Find("[data-testid='person-birthday-reminder-disabled']").Change(true);
        Save(cut);

        cut.WaitForAssertion(() => people.Created.Should().ContainSingle());
        var request = people.Created.Single();
        request.FirstName.Should().Be("Grace");
        request.BirthdayReminderDisabled.Should().BeTrue();
    }
}
