using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.People;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

public class GlobalSearchTests
{
    private static BunitContext CreateContext(FakePeopleService people, out IRenderedComponent<MudPopoverProvider> popovers)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPeopleService>(people);
        popovers = context.Render<MudPopoverProvider>();
        return context;
    }

    [Fact]
    public async Task GlobalSearch_renders_search_input()
    {
        var people = new FakePeopleService();
        await using var context = CreateContext(people, out _);

        var cut = context.Render<GlobalSearch>();

        cut.Find("[data-testid='global-search']").Should().NotBeNull();
        var input = cut.Find("input");
        input.GetAttribute("placeholder").Should().Be("Search people...");
    }

    [Fact]
    public async Task Typing_in_global_search_calls_PeopleService_SearchAsync()
    {
        var people = new FakePeopleService
        {
            SearchResult = (query, limit, includeArchived) =>
                [new PersonSearchResult(Guid.NewGuid(), "Ada", "Lovelace", "Friend", false)]
        };
        await using var context = CreateContext(people, out var popovers);

        var cut = context.Render<GlobalSearch>();
        var autocomplete = cut.FindComponent<MudAutocomplete<PersonSearchResult>>();

        Func<string, CancellationToken, Task<IEnumerable<PersonSearchResult>>> searchFunc = autocomplete.Instance.SearchFunc!;
        var results = await searchFunc("Ada", CancellationToken.None);

        results.Should().ContainSingle();
        results.First().DisplayName.Should().Be("Ada Lovelace");
        people.SearchQueries.Should().ContainSingle().Which.Query.Should().Be("Ada");
    }

    [Fact]
    public async Task Selecting_person_navigates_to_their_profile()
    {
        var targetId = Guid.NewGuid();
        var people = new FakePeopleService
        {
            SearchResult = (query, limit, includeArchived) =>
                [new PersonSearchResult(targetId, "Ada", "Lovelace", null, false)]
        };
        await using var context = CreateContext(people, out _);

        var cut = context.Render<GlobalSearch>();
        var autocomplete = cut.FindComponent<MudAutocomplete<PersonSearchResult>>();

        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(
            new PersonSearchResult(targetId, "Ada", "Lovelace", null, false)));

        var nav = context.Services.GetRequiredService<NavigationManager>();
        var uri = new Uri(nav.Uri);
        uri.PathAndQuery.Should().Be($"/people/{targetId}");
    }
}
