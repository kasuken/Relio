using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

/// <summary>
/// The person form in edit mode (issue #24), and the contact methods and tags it now carries in
/// both modes. The fakes stand in for the services: what is asserted is what the form sends and how
/// it words what the service says back.
/// </summary>
public class PersonFormEditTests
{
    private static BunitContext CreateContext(
        FakePeopleService people,
        FakeTagService tags,
        out IRenderedComponent<MudSnackbarProvider> snackbars,
        out IRenderedComponent<MudPopoverProvider> popovers)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        var types = new FakeRelationshipTypeService();
        types.Types.AddRange(RelationshipType.CreateDefaults("user"));
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IRelationshipTypeService>(types);
        context.Services.AddSingleton<ITagService>(tags);
        popovers = context.Render<MudPopoverProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    private static Person PersonWith(
        Action<Person>? configure = null)
    {
        var person = new Person { FirstName = "Ada", LastName = "Lovelace" };
        configure?.Invoke(person);
        return person;
    }

    private static ContactMethod Contact(
        ContactMethodKind kind, string value, string? label = null, int sortOrder = 0) =>
        new() { Kind = kind, Value = value, Label = label, SortOrder = sortOrder };

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<PersonForm> cut) =>
        cut.FindAll("[data-testid='person-contact-method-row']");

    private static IElement ValueInput(IElement row) =>
        row.QuerySelector("[data-testid='person-contact-method-value-field'] input, [data-testid='person-contact-method-value-field'] textarea")!;

    private static void Save(IRenderedComponent<PersonForm> cut) =>
        cut.Find("[data-testid='person-form-save']").Click();

    private static void AddRow(IRenderedComponent<PersonForm> cut) =>
        cut.Find("[data-testid='person-contact-method-add']").Click();

    [Fact]
    public async Task Edit_mode_starts_from_the_person_and_saves_with_UpdateAsync()
    {
        var chess = new Tag { Name = "Chess" };
        var existing = Contact(ContactMethodKind.Email, "ada@example.com", "Work");
        var person = PersonWith(p =>
        {
            p.Nickname = "Countess";
            p.ContactMethods.Add(existing);
            p.Tags.Add(chess);
        });
        var people = new FakePeopleService();
        var tags = new FakeTagService();
        tags.Tags.Add(chess);
        await using var context = CreateContext(people, tags, out var snackbars, out _);
        Guid? saved = null;
        var cut = context.Render<PersonForm>(parameters => parameters
            .Add(p => p.Person, person)
            .Add(p => p.OnSaved, (Guid id) => saved = id));

        cut.Find("[data-testid='person-first-name-field'] input").GetAttribute("value").Should().Be("Ada");
        cut.Find("[data-testid='person-nickname-field'] input").GetAttribute("value").Should().Be("Countess");
        ValueInput(Rows(cut).Single()).GetAttribute("value").Should().Be("ada@example.com");
        cut.FindAll("[data-testid='person-tag-chip']").Select(c => c.TextContent.Trim()).Should().Equal("Chess");
        cut.Find("[data-testid='person-form-save']").TextContent.Trim().Should().Be("Save changes");
        cut.Find("[data-testid='person-form-cancel']").GetAttribute("href").Should().Be($"/people/{person.Id}");

        cut.Find("[data-testid='person-first-name-field'] input").Input("Augusta");
        Save(cut);

        cut.WaitForAssertion(() => people.Updated.Should().ContainSingle());
        var (personId, request) = people.Updated.Single();
        personId.Should().Be(person.Id);
        request.FirstName.Should().Be("Augusta");
        request.ContactMethods.Should().ContainSingle().Which.Should().Be(
            new ContactMethodInput(existing.Id, ContactMethodKind.Email, "Work", "ada@example.com"));
        request.TagIds.Should().Equal(chess.Id);
        request.NewTagNames.Should().BeEmpty();
        people.Created.Should().BeEmpty("an edit never creates");
        cut.WaitForAssertion(() => saved.Should().Be(person.Id));
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Changes saved"));
    }

    [Fact]
    public async Task Add_mode_also_sends_the_contact_methods_and_tags()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, new FakeTagService(), out _, out _);
        var cut = context.Render<PersonForm>();
        cut.Find("[data-testid='person-first-name-field'] input").Input("Ada");
        AddRow(cut);
        ValueInput(Rows(cut).Single()).Input("ada@example.com");

        Save(cut);

        cut.WaitForAssertion(() => people.Created.Should().ContainSingle());
        people.Created.Single().ContactMethods.Should().ContainSingle().Which.Should().Be(
            new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com"));
        cut.Find("[data-testid='person-form-save']").TextContent.Trim().Should().Be("Add person");
        cut.Find("[data-testid='person-form-cancel']").GetAttribute("href").Should().Be("/people");
    }

    [Fact]
    public async Task Removing_a_contact_method_row_leaves_it_out_of_the_request()
    {
        var keep = Contact(ContactMethodKind.Email, "keep@example.com", sortOrder: 0);
        var drop = Contact(ContactMethodKind.Phone, "+44 7700 900123", sortOrder: 1);
        var person = PersonWith(p => { p.ContactMethods.Add(keep); p.ContactMethods.Add(drop); });
        var people = new FakePeopleService();
        await using var context = CreateContext(people, new FakeTagService(), out _, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));
        Rows(cut).Should().HaveCount(2);

        cut.FindAll("[data-testid='person-contact-method-remove']")[1].Click();
        Save(cut);

        Rows(cut).Should().HaveCount(1);
        cut.WaitForAssertion(() => people.Updated.Should().ContainSingle());
        people.Updated.Single().Request.ContactMethods!.Select(c => c.Id).Should().Equal(keep.Id);
    }

    [Fact]
    public async Task Adding_a_row_defaults_to_email_and_a_blank_row_is_not_sent()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, new FakeTagService(), out _, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, PersonWith()));

        AddRow(cut);
        AddRow(cut);
        Rows(cut).Should().HaveCount(2);
        Rows(cut)[0].QuerySelector("[data-testid='person-contact-method-kind-field'] input")!.GetAttribute("value").Should().Be("Email");
        ValueInput(Rows(cut)[1]).Input("ada@example.com");
        Save(cut);

        cut.WaitForAssertion(() => people.Updated.Should().ContainSingle());
        people.Updated.Single().Request.ContactMethods.Should().ContainSingle()
            .Which.Value.Should().Be("ada@example.com");
    }

    [Fact]
    public async Task A_label_typed_into_a_row_is_sent()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, new FakeTagService(), out _, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, PersonWith()));
        AddRow(cut);
        var row = Rows(cut).Single();
        ValueInput(row).Input("ada@example.com");

        var label = row.QuerySelector("[data-testid='person-contact-method-label-field'] input")!;
        label.Input("Mum's work");
        label.Blur();
        Save(cut);

        cut.WaitForAssertion(() => people.Updated.Should().ContainSingle());
        people.Updated.Single().Request.ContactMethods.Should().ContainSingle().Which.Label.Should().Be("Mum's work");
    }

    [Fact]
    public async Task The_value_field_uses_an_email_or_telephone_input_with_autocomplete_off()
    {
        var person = PersonWith(p =>
        {
            p.ContactMethods.Add(Contact(ContactMethodKind.Email, "ada@example.com", sortOrder: 0));
            p.ContactMethods.Add(Contact(ContactMethodKind.Phone, "+44 7700 900123", sortOrder: 1));
            p.ContactMethods.Add(Contact(ContactMethodKind.Address, "1 Example Square", sortOrder: 2));
            p.ContactMethods.Add(Contact(ContactMethodKind.Social, "@ada", sortOrder: 3));
        });
        await using var context = CreateContext(new FakePeopleService(), new FakeTagService(), out _, out _);

        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));

        var inputs = Rows(cut).Select(ValueInput).ToList();
        inputs.Select(i => i.TagName.ToLowerInvariant()).Should().Equal("input", "input", "textarea", "input");
        inputs.Select(i => i.GetAttribute("type")).Should().Equal("email", "tel", "text", "text");
        inputs.Should().OnlyContain(i => i.GetAttribute("autocomplete") == "off", "a browser must not autofill someone else's details");
    }

    [Fact]
    public async Task Changing_the_kind_changes_the_value_field_and_what_it_is_called()
    {
        var person = PersonWith(p => p.ContactMethods.Add(Contact(ContactMethodKind.Email, "ada@example.com")));
        await using var context = CreateContext(new FakePeopleService(), new FakeTagService(), out _, out var popovers);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));
        var row = Rows(cut).Single();
        row.QuerySelector("[data-testid='person-contact-method-value-field']")!.TextContent.Should().Contain("Email address");

        row.QuerySelector("[data-testid='person-contact-method-kind-field'] .mud-input-control")!.MouseDown();
        var items = popovers.WaitForElements(".mud-list-item");
        items.Select(i => i.TextContent.Trim()).Should().Equal("Email", "Phone", "Address", "Social", "Other");
        items.Single(i => i.TextContent.Trim() == "Phone").Click();

        cut.WaitForAssertion(() =>
        {
            var changed = Rows(cut).Single();
            changed.QuerySelector("[data-testid='person-contact-method-value-field']")!.TextContent.Should().Contain("Phone number");
            ValueInput(changed).GetAttribute("type").Should().Be("tel");
        });
    }

    [Fact]
    public async Task Contact_method_errors_show_under_the_right_row_when_blank_rows_are_skipped()
    {
        var people = new FakePeopleService
        {
            // Index 0 in the request is the SECOND row on screen: the first was never filled in.
            ThrowOnNextUpdate = new PersonValidationException(
                [], [new ContactMethodProblem(0, ContactMethodValidationError.EmailInvalid)]),
        };
        await using var context = CreateContext(people, new FakeTagService(), out _, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, PersonWith()));
        AddRow(cut);
        AddRow(cut);
        ValueInput(Rows(cut)[1]).Input("not an email");

        Save(cut);

        cut.WaitForAssertion(() =>
            Rows(cut)[1].TextContent.Should().Contain("Enter an email address like name@example.com."));
        Rows(cut)[0].TextContent.Should().NotContain("Enter an email address");
    }

    [Fact]
    public async Task A_missing_value_is_worded_for_the_kind_of_row()
    {
        var existing = Contact(ContactMethodKind.Phone, "+44 7700 900123");
        var people = new FakePeopleService
        {
            ThrowOnNextUpdate = new PersonValidationException(
                [], [new ContactMethodProblem(0, ContactMethodValidationError.ValueRequired)]),
        };
        await using var context = CreateContext(people, new FakeTagService(), out _, out _);
        var cut = context.Render<PersonForm>(parameters => parameters
            .Add(p => p.Person, PersonWith(p => p.ContactMethods.Add(existing))));
        ValueInput(Rows(cut).Single()).Input("");

        Save(cut);

        cut.WaitForAssertion(() =>
            Rows(cut).Single().TextContent.Should().Contain("Enter a phone number, or remove this row."));
        people.Updated.Should().BeEmpty("the fake threw before recording");
    }

    [Fact]
    public async Task Too_many_contact_methods_is_explained_under_the_group()
    {
        var people = new FakePeopleService
        {
            ThrowOnNextUpdate = new PersonValidationException([PersonValidationError.TooManyContactMethods]),
        };
        await using var context = CreateContext(people, new FakeTagService(), out _, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, PersonWith()));

        Save(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-contact-methods-error']").TextContent
            .Should().Contain("A person can have up to 20 contact methods. Remove one to add another."));
    }

    [Fact]
    public async Task The_add_button_is_disabled_at_20_contact_methods()
    {
        var person = PersonWith(p =>
        {
            for (var i = 0; i < 20; i++)
            {
                p.ContactMethods.Add(Contact(ContactMethodKind.Other, $"detail {i}", sortOrder: i));
            }
        });
        await using var context = CreateContext(new FakePeopleService(), new FakeTagService(), out _, out _);

        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));

        cut.Find("[data-testid='person-contact-method-add']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='person-contact-methods']").TextContent
            .Should().Contain("A person can have up to 20 contact methods.");

        cut.FindAll("[data-testid='person-contact-method-remove']")[0].Click();

        cut.Find("[data-testid='person-contact-method-add']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task A_stale_contact_method_shows_the_reload_notice()
    {
        var people = new FakePeopleService
        {
            ThrowOnNextUpdate = new ForeignEntityNotOwnedException(ForeignEntityNames.ContactMethods),
        };
        var person = PersonWith(p => p.ContactMethods.Add(Contact(ContactMethodKind.Email, "ada@example.com")));
        await using var context = CreateContext(people, new FakeTagService(), out var snackbars, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));
        cut.FindAll("[data-testid='person-form-stale']").Should().BeEmpty();

        Save(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-form-stale']").TextContent
            .Should().Contain("This profile changed in another tab or window. Reload to see the latest version, then make your changes again."));
        snackbars.Markup.Should().NotContain("Changes saved");

        cut.Find("[data-testid='person-form-reload']").Click();

        var navigation = (BunitNavigationManager)context.Services.GetRequiredService<NavigationManager>();
        navigation.History.Should().ContainSingle().Which.Options.ForceLoad.Should().BeTrue("Reload asks the server for the page again");
    }

    [Fact]
    public async Task A_removed_tag_shows_a_problem_and_is_dropped()
    {
        var kept = new Tag { Name = "Chess" };
        var gone = new Tag { Name = "Climbing" };
        var people = new FakePeopleService { ThrowOnNextUpdate = new ForeignEntityNotOwnedException(ForeignEntityNames.Tags) };
        var tags = new FakeTagService();
        tags.Tags.AddRange([kept, gone]);
        var person = PersonWith(p => { p.Tags.Add(kept); p.Tags.Add(gone); });
        await using var context = CreateContext(people, tags, out var snackbars, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));
        var callsBefore = tags.ListCalls;
        tags.Tags.Remove(gone);

        Save(cut);

        snackbars.WaitForAssertion(() => snackbars.Markup.Should()
            .Contain("One of those tags was removed. Check the tags and save again."));
        tags.ListCalls.Should().BeGreaterThan(callsBefore);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='person-tag-chip']")
            .Select(c => c.TextContent.Trim()).Should().Equal("Chess"));
    }

    [Fact]
    public async Task A_tag_name_conflict_reloads_the_tags_and_explains_it()
    {
        var people = new FakePeopleService
        {
            ThrowOnNextUpdate = new PersonValidationException([PersonValidationError.TagNameConflict]),
        };
        var tags = new FakeTagService();
        await using var context = CreateContext(people, tags, out _, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, PersonWith()));
        var callsBefore = tags.ListCalls;

        Save(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-tags-field']").TextContent
            .Should().Contain("A tag with a very similar name already exists. Choose it from the list."));
        tags.ListCalls.Should().BeGreaterThan(callsBefore);
    }

    [Fact]
    public async Task An_update_for_a_missing_person_raises_OnNotFound()
    {
        var people = new FakePeopleService { UpdateResult = false };
        await using var context = CreateContext(people, new FakeTagService(), out var snackbars, out _);
        var notFound = false;
        var saved = false;
        var cut = context.Render<PersonForm>(parameters => parameters
            .Add(p => p.Person, PersonWith())
            .Add(p => p.OnNotFound, () => notFound = true)
            .Add(p => p.OnSaved, (Guid _) => saved = true));

        Save(cut);

        cut.WaitForAssertion(() => notFound.Should().BeTrue());
        saved.Should().BeFalse();
        snackbars.Markup.Should().NotContain("Changes saved");
    }

    [Fact]
    public async Task An_unavailable_relationship_type_is_still_handled_in_edit_mode()
    {
        var people = new FakePeopleService
        {
            ThrowOnNextUpdate = new ForeignEntityNotOwnedException(ForeignEntityNames.RelationshipTypes),
        };
        await using var context = CreateContext(people, new FakeTagService(), out var snackbars, out _);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, PersonWith()));

        Save(cut);

        snackbars.WaitForAssertion(() => snackbars.Markup.Should()
            .Contain("That relationship type is no longer available. Choose another one."));
        cut.FindAll("[data-testid='person-form-stale']").Should().BeEmpty();
    }

    [Fact]
    public async Task No_button_in_the_form_submits_it()
    {
        var person = PersonWith(p =>
        {
            p.ContactMethods.Add(Contact(ContactMethodKind.Email, "ada@example.com"));
            p.Tags.Add(new Tag { Name = "Chess" });
        });
        await using var context = CreateContext(new FakePeopleService(), new FakeTagService(), out _, out _);

        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, person));

        // Enter inside an autocomplete would otherwise submit the whole form and save a half-typed tag.
        cut.FindAll("[type='submit']").Should().BeEmpty();
        cut.FindAll("button").Should().OnlyContain(b => b.GetAttribute("type") != "submit");
        cut.Find("[data-testid='person-form']").HasAttribute("novalidate").Should().BeTrue();
    }

    [Fact]
    public async Task Typing_a_new_tag_adds_a_chip_and_sends_it_as_a_new_name()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, new FakeTagService(), out _, out var popovers);
        var cut = context.Render<PersonForm>(parameters => parameters.Add(p => p.Person, PersonWith()));

        cut.Find("[data-testid='person-tags-field'] input").Input("Climbing");
        popovers.WaitForElements(".mud-popover-open .mud-list-item")
            .Single(o => o.TextContent.Contains("Create tag")).Click();
        Save(cut);

        cut.WaitForAssertion(() => people.Updated.Should().ContainSingle());
        var request = people.Updated.Single().Request;
        request.NewTagNames.Should().Equal("Climbing");
        request.TagIds.Should().BeEmpty();
    }
}
