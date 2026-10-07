using Relio.Application.Interactions;
using Relio.Web.Components.Interactions;

namespace Relio.Web.Tests.Interactions;

public sealed class QuickLogPersonSearchTests
{
    private static InteractionParticipantOption Person(string displayName, bool archived = false) =>
        new(Guid.NewGuid(), displayName, archived);

    [Fact]
    public void Filter_matches_normalized_names_and_puts_prefixes_first()
    {
        var prefix = Person("José García");
        var contains = Person("Jean José");
        var archived = Person("José Archived", archived: true);

        QuickLogPersonSearch.Filter([contains, archived, prefix], "jose")
            .Should()
            .Equal(prefix, contains);
    }

    [Fact]
    public void Filter_without_search_text_returns_only_the_first_twenty_active_people()
    {
        var people = Enumerable.Range(0, 30)
            .Select(index => Person($"Person {index:00}", archived: index % 3 == 0))
            .ToArray();
        var expected = people.Where(person => !person.IsArchived).Take(QuickLogPersonSearch.DefaultMax);

        QuickLogPersonSearch.Filter(people, null).Should().Equal(expected);
    }

    [Fact]
    public void Filter_caps_nonempty_search_results()
    {
        var people = Enumerable.Range(0, 30)
            .Select(index => Person($"Ada Person {index:00}"))
            .ToArray();

        QuickLogPersonSearch.Filter(people, "ada", max: 5).Should().Equal(people.Take(5));
    }
}
