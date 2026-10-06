using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Tests.Shared;
using PeoplePage = Relio.Web.Components.Pages.People;

namespace Relio.Web.Tests.People;

public class PeoplePageTests
{
    private static BunitContext CreateContext(FakePeopleService people)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPeopleService>(people);
        return context;
    }

    [Fact]
    public async Task Shows_the_empty_state_with_Add_a_person_when_there_are_no_people()
    {
        await using var context = CreateContext(new FakePeopleService());

        var cut = context.Render<PeoplePage>();

        cut.Find("h1").TextContent.Should().Be("No one here yet");
        cut.Markup.Should().Contain("Add the first person you want to keep in touch with.");
        cut.Find("button").TextContent.Trim().Should().Be("Add a person");
        cut.FindAll("[data-testid='people-list']").Should().BeEmpty();
    }

    [Fact]
    public async Task The_empty_state_action_navigates_to_the_create_page()
    {
        await using var context = CreateContext(new FakePeopleService());
        var cut = context.Render<PeoplePage>();

        cut.Find("button").Click();

        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/people/new");
    }

    [Fact]
    public async Task Lists_people_with_their_relationship_type_and_links_to_their_profile()
    {
        var people = new FakePeopleService();
        var friend = new RelationshipType { Name = "Friend" };
        var ada = new Person { FirstName = "Ada", LastName = "Lovelace", RelationshipType = friend, RelationshipTypeId = friend.Id };
        var grace = new Person { FirstName = "Grace" };
        people.Known.AddRange([ada, grace]);
        await using var context = CreateContext(people);

        var cut = context.Render<PeoplePage>();

        cut.Find("h1").TextContent.Should().Be("People");
        cut.FindAll("h1").Should().ContainSingle("the page has exactly one h1");
        var rows = cut.FindAll("[data-testid='people-list-item']");
        rows.Should().HaveCount(2);
        rows[0].GetAttribute("href").Should().Be($"/people/{ada.Id}");
        rows[0].QuerySelector("[data-testid='people-list-item-name']")!.TextContent.Should().Be("Ada Lovelace");
        rows[0].QuerySelector("[data-testid='people-list-item-relationship']")!.TextContent.Should().Be("Friend");
        rows[0].TextContent.Should().Contain("AL", "the monogram");
        rows[1].GetAttribute("href").Should().Be($"/people/{grace.Id}");
        rows[1].QuerySelector("[data-testid='people-list-item-relationship']").Should().BeNull();
        cut.Find("[data-testid='people-add']").GetAttribute("href").Should().Be("/people/new");
    }

    [Fact]
    public async Task Hides_archived_people()
    {
        var people = new FakePeopleService();
        people.Known.Add(new Person { FirstName = "Sam", IsArchived = true });
        await using var context = CreateContext(people);

        var cut = context.Render<PeoplePage>();

        cut.Find("h1").TextContent.Should().Be("No one here yet");
    }
}
