using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.Pages;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

public class PersonProfilePageTests
{
    private static BunitContext CreateContext(FakePeopleService people)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPeopleService>(people);
        return context;
    }

    [Fact]
    public async Task Shows_not_found_when_the_service_returns_null()
    {
        await using var context = CreateContext(new FakePeopleService());

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, Guid.NewGuid()));

        cut.Find("[data-testid='person-not-found']").TextContent.Should().Contain("This person isn't in your list");
        cut.Markup.Should().Contain("The link may be wrong, or they were removed.");
        cut.FindAll("[data-testid='person-name']").Should().BeEmpty();
        cut.FindAll("h1").Should().ContainSingle();
    }

    [Fact]
    public async Task The_not_found_action_goes_back_to_the_people_list()
    {
        await using var context = CreateContext(new FakePeopleService());
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, Guid.NewGuid()));

        cut.Find("[data-testid='person-not-found'] button").Click();

        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/people");
    }

    [Fact]
    public async Task Shows_the_saved_details_including_a_yearless_birthday()
    {
        var friend = new RelationshipType { Name = "Friend" };
        var person = new Person
        {
            FirstName = "Liam",
            LastName = "Chen",
            Nickname = "Lee",
            RelationshipType = friend,
            RelationshipTypeId = friend.Id,
            BirthdayDay = 14,
            BirthdayMonth = 3,
            HowWeMet = "At the climbing wall.",
            Details = "Line one.\nLine two.",
        };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Liam Chen");
        cut.Find("h1").TextContent.Should().Be("Liam Chen");
        cut.Find("[data-testid='person-relationship']").TextContent.Should().Be("Friend");
        cut.Find("[data-testid='person-nickname']").TextContent.Should().Be("Lee");
        cut.Find("[data-testid='person-birthday']").TextContent.Should().Be("14 March");
        cut.Find("[data-testid='person-how-we-met']").TextContent.Should().Be("At the climbing wall.");
        cut.Find("[data-testid='person-details']").TextContent.Should().Be("Line one.\nLine two.");
        cut.Find("[data-testid='person-details']").ClassList.Should().Contain("rl-multiline");
        cut.Find("[data-testid='person-back']").GetAttribute("href").Should().Be("/people");
    }

    [Fact]
    public async Task Shows_a_birthday_with_its_year()
    {
        var person = new Person { FirstName = "Ada", BirthdayDay = 10, BirthdayMonth = 12, BirthdayYear = 1815 };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-birthday']").TextContent.Should().Be("10 December 1815");
    }

    [Fact]
    public async Task Leaves_out_everything_that_is_not_set()
    {
        var person = new Person { FirstName = "Grace" };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Grace");
        foreach (var field in new[] { "person-relationship", "person-nickname", "person-birthday", "person-how-we-met", "person-details" })
        {
            cut.FindAll($"[data-testid='{field}']").Should().BeEmpty($"{field} is not set");
        }
    }

    [Fact]
    public async Task Renders_user_text_as_text_not_markup()
    {
        var person = new Person { FirstName = "Ada", Details = "<script>alert(1)</script><b>bold</b>" };
        var people = new FakePeopleService();
        people.Known.Add(person);
        await using var context = CreateContext(people);

        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, person.Id));

        cut.Find("[data-testid='person-details']").TextContent.Should().Be("<script>alert(1)</script><b>bold</b>");
        cut.FindAll("script").Should().BeEmpty();
        cut.FindAll("b").Should().BeEmpty();
    }

    [Fact]
    public async Task Loads_the_new_person_when_the_route_parameter_changes()
    {
        var first = new Person { FirstName = "Ada" };
        var second = new Person { FirstName = "Grace" };
        var people = new FakePeopleService();
        people.Known.AddRange([first, second]);
        await using var context = CreateContext(people);
        var cut = context.Render<PersonProfile>(parameters => parameters.Add(p => p.PersonId, first.Id));
        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Ada");

        cut.Render(parameters => parameters.Add(p => p.PersonId, second.Id));

        cut.Find("[data-testid='person-name']").TextContent.Should().Be("Grace");
    }
}
