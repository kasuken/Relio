using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.Pages;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

public class EditPersonPageTests
{
    private static BunitContext CreateContext(FakePeopleService people)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        var types = new FakeRelationshipTypeService();
        types.Types.AddRange(RelationshipType.CreateDefaults("user"));
        context.Services.AddSingleton<IPeopleService>(people);
        context.Services.AddSingleton<IRelationshipTypeService>(types);
        context.Services.AddSingleton<ITagService>(new FakeTagService());
        context.Render<MudPopoverProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Shows_not_found_when_the_service_returns_null()
    {
        await using var context = CreateContext(new FakePeopleService());

        var cut = context.Render<EditPerson>(parameters => parameters.Add(p => p.PersonId, Guid.NewGuid()));

        cut.Find("[data-testid='person-not-found']").TextContent.Should().Contain("This person isn't in your list");
        cut.FindAll("[data-testid='person-form']").Should().BeEmpty();
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task Prefills_the_form_and_has_exactly_one_h1()
    {
        var person = new Person
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            ContactMethods = { new ContactMethod { Kind = ContactMethodKind.Email, Value = "ada@example.com" } },
        };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<EditPerson>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.FindAll("h1").Should().ContainSingle().Which.TextContent.Should().Be("Edit details",
            "the heading is generic: a name in a heading would land in the screen reader's page list and history");
        cut.Find("[data-testid='person-first-name-field'] input").GetAttribute("value").Should().Be("Ada");
        cut.FindAll("[data-testid='person-contact-method-row']").Should().ContainSingle();
        cut.Find("[data-testid='person-edit-back']").GetAttribute("href").Should().Be($"/people/{person.Id}");
        cut.Find("[data-testid='person-form-cancel']").GetAttribute("href").Should().Be($"/people/{person.Id}");
        cut.Markup.Should().NotContain("<title>", "the title is set by PageTitle, which is generic");
    }

    [Fact]
    public async Task Saving_navigates_to_the_profile()
    {
        var person = new Person { FirstName = "Ada" };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);
        var cut = context.Render<EditPerson>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-form-save']").Click();

        cut.WaitForAssertion(() => context.Services.GetRequiredService<NavigationManager>().Uri
            .Should().EndWith($"/people/{person.Id}"));
        people.Updated.Should().ContainSingle().Which.PersonId.Should().Be(person.Id);
    }

    [Fact]
    public async Task A_person_that_disappears_while_editing_turns_the_page_into_the_not_found_panel()
    {
        var person = new Person { FirstName = "Ada" };
        var people = new FakePeopleService { UpdateResult = false };
        people.Known.Add(person);
        await using var context = CreateContext(people);
        var cut = context.Render<EditPerson>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-form-save']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='person-not-found']").Should().NotBeNull());
        cut.FindAll("[data-testid='person-form']").Should().BeEmpty();
    }

    [Fact]
    public async Task Loads_the_other_person_when_the_route_parameter_changes()
    {
        var first = new Person { FirstName = "Ada" };
        var second = new Person { FirstName = "Grace" };
        var people = new FakePeopleService();
        people.Known.AddRange([first, second]);
        await using var context = CreateContext(people);
        var cut = context.Render<EditPerson>(parameters => parameters.Add(p => p.PersonId, first.Id));
        cut.Find("[data-testid='person-first-name-field'] input").Input("Edited but not saved");

        cut.Render(parameters => parameters.Add(p => p.PersonId, second.Id));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='person-first-name-field'] input").GetAttribute("value").Should().Be("Grace"));
    }
}
