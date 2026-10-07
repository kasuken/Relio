using Relio.Application.People;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class MergeCandidateSearchTests
{
    private static PersonSummary Person(string first, string? last = null) => new(Guid.NewGuid(), first, last, false);

    [Fact]
    public void Filter_ignores_case_and_accents()
    {
        var jose = Person("José", "García");
        var people = new[] { Person("Ada", "Lovelace"), jose };

        MergeCandidateSearch.Filter(people, "jose").Should().Equal(jose);
        MergeCandidateSearch.Filter(people, "GARCIA").Should().Equal(jose);
        MergeCandidateSearch.Filter(people, "  josé   garcía ").Should().Equal(jose);
    }

    [Fact]
    public void Filter_puts_names_that_start_with_the_text_first()
    {
        var anna = Person("Anna", "Lee");
        var joan = Person("Joan", "Smith");
        var jon = Person("Jon", "Smith");
        var bjon = Person("Bjon");

        var result = MergeCandidateSearch.Filter([anna, joan, bjon, jon], "jo");

        result.Should().Equal(joan, jon, bjon);
    }

    [Fact]
    public void Filter_matches_across_first_and_last_name()
    {
        var jon = Person("Jon", "Smith");

        MergeCandidateSearch.Filter([jon, Person("Ada")], "jon sm").Should().Equal(jon);
    }

    [Fact]
    public void Filter_with_no_text_returns_the_first_twenty()
    {
        var people = Enumerable.Range(0, 30).Select(i => Person($"Person{i:00}")).ToList();

        MergeCandidateSearch.Filter(people, null).Should().Equal(people.Take(20));
        MergeCandidateSearch.Filter(people, "   ").Should().Equal(people.Take(20));
    }

    [Fact]
    public void Filter_caps_the_results()
    {
        var people = Enumerable.Range(0, 30).Select(i => Person($"Jon{i:00}")).ToList();

        MergeCandidateSearch.Filter(people, "jon").Should().HaveCount(20);
        MergeCandidateSearch.Filter(people, "jon", max: 5).Should().Equal(people.Take(5));
    }

    [Fact]
    public void Filter_finds_nothing_for_a_name_nobody_has()
    {
        MergeCandidateSearch.Filter([Person("Ada")], "zzz").Should().BeEmpty();
    }
}
