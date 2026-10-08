using Relio.Application.People;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PeopleListQueryStringTests
{
    [Fact]
    public void Parse_with_no_values_returns_the_defaults()
    {
        var query = PeopleListQueryString.Parse(null, null, null);

        query.Should().Be(new PeopleListQuery());
        query.Sort.Should().Be(PeopleSort.Name);
        query.IncludeArchived.Should().BeFalse();
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(PeopleListQuery.DefaultPageSize);
    }

    [Theory]
    [InlineData("name", PeopleSort.Name)]
    [InlineData("added", PeopleSort.RecentlyAdded)]
    [InlineData("contacted", PeopleSort.LastContacted)]
    [InlineData("ADDED", PeopleSort.RecentlyAdded)]
    [InlineData(" Contacted ", PeopleSort.LastContacted)]
    public void Parse_reads_each_sort_token_case_insensitively(string value, PeopleSort expected)
    {
        PeopleListQueryString.Parse(value, null, null).Sort.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("1")]
    [InlineData("RecentlyAdded")]
    public void Parse_falls_back_to_name_for_an_unknown_sort(string value)
    {
        PeopleListQueryString.Parse(value, null, null).Sort.Should().Be(PeopleSort.Name);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Parse_reads_archived_only_from_true(string? value, bool expected)
    {
        PeopleListQueryString.Parse(null, value, null).IncludeArchived.Should().Be(expected);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("40", 40)]
    public void Parse_reads_a_positive_page(string value, int expected)
    {
        PeopleListQueryString.Parse(null, null, value).Page.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(" 2")]
    [InlineData("2.5")]
    [InlineData("99999999999")]
    [InlineData(null)]
    public void Parse_falls_back_to_page_one_for_an_invalid_page(string? value)
    {
        PeopleListQueryString.Parse(null, null, value).Page.Should().Be(1);
    }

    [Fact]
    public void ToRelativeUri_leaves_defaults_out()
    {
        PeopleListQueryString.ToRelativeUri(new PeopleListQuery()).Should().Be("/people");
        PeopleListQueryString.ToRelativeUri(new PeopleListQuery { Sort = PeopleSort.Name, Page = 1 }).Should().Be("/people");
    }

    [Theory]
    [InlineData(PeopleSort.RecentlyAdded, false, 1, "/people?sort=added")]
    [InlineData(PeopleSort.Name, true, 1, "/people?archived=true")]
    [InlineData(PeopleSort.Name, false, 3, "/people?page=3")]
    [InlineData(PeopleSort.LastContacted, true, 2, "/people?sort=contacted&archived=true&page=2")]
    [InlineData(PeopleSort.RecentlyAdded, true, 1, "/people?sort=added&archived=true")]
    public void ToRelativeUri_writes_parameters_in_a_fixed_order(PeopleSort sort, bool archived, int page, string expected)
    {
        var query = new PeopleListQuery { Sort = sort, IncludeArchived = archived, Page = page };

        PeopleListQueryString.ToRelativeUri(query).Should().Be(expected);
    }

    [Theory]
    [InlineData(PeopleSort.Name, false, 1)]
    [InlineData(PeopleSort.RecentlyAdded, false, 1)]
    [InlineData(PeopleSort.LastContacted, true, 7)]
    [InlineData(PeopleSort.Name, true, 2)]
    public void Parse_and_ToRelativeUri_round_trip(PeopleSort sort, bool archived, int page)
    {
        var query = new PeopleListQuery { Sort = sort, IncludeArchived = archived, Page = page };
        var uri = new Uri("http://localhost" + PeopleListQueryString.ToRelativeUri(query));
        var values = System.Web.HttpUtility.ParseQueryString(uri.Query);

        PeopleListQueryString.Parse(
            values[PeopleListQueryString.SortParameter],
            values[PeopleListQueryString.ArchivedParameter],
            values[PeopleListQueryString.PageParameter]).Should().Be(query);
    }

    [Fact]
    public void Parse_reads_search_tags_and_relationship_types()
    {
        var query = PeopleListQueryString.Parse(
            "added",
            "true",
            "2",
            "Ada",
            ["Family", "Friends"],
            ["Colleague"]);

        query.Sort.Should().Be(PeopleSort.RecentlyAdded);
        query.IncludeArchived.Should().BeTrue();
        query.Page.Should().Be(2);
        query.SearchTerm.Should().Be("Ada");
        query.Tags.Should().Equal("Family", "Friends");
        query.RelationshipTypes.Should().Equal("Colleague");
    }

    [Fact]
    public void Parse_handles_comma_separated_tags_and_types()
    {
        var query = PeopleListQueryString.Parse(
            null,
            null,
            null,
            "  Lovelace  ",
            tag: "Family, Friends, Family",
            type: "Colleague, Mentor");

        query.SearchTerm.Should().Be("Lovelace");
        query.Tags.Should().Equal("Family", "Friends");
        query.RelationshipTypes.Should().Equal("Colleague", "Mentor");
    }

    [Fact]
    public void ToRelativeUri_includes_search_tags_and_relationship_types()
    {
        var query = new PeopleListQuery
        {
            SearchTerm = "Ada",
            Tags = ["Family", "Close Friends"],
            RelationshipTypes = ["Colleague"],
        };

        var uri = PeopleListQueryString.ToRelativeUri(query);
        uri.Should().Be("/people?q=Ada&tag=Family&tag=Close%20Friends&type=Colleague");
    }

    [Fact]
    public void Parse_and_ToRelativeUri_round_trip_with_filters()
    {
        var query = new PeopleListQuery
        {
            Sort = PeopleSort.LastContacted,
            IncludeArchived = true,
            Page = 2,
            SearchTerm = "Grace",
            Tags = ["Tech", "History"],
            RelationshipTypes = ["Friend"],
        };

        var uri = new Uri("http://localhost" + PeopleListQueryString.ToRelativeUri(query));
        var values = System.Web.HttpUtility.ParseQueryString(uri.Query);

        PeopleListQueryString.Parse(
            values[PeopleListQueryString.SortParameter],
            values[PeopleListQueryString.ArchivedParameter],
            values[PeopleListQueryString.PageParameter],
            values[PeopleListQueryString.SearchParameter],
            values.GetValues(PeopleListQueryString.TagParameter),
            values.GetValues(PeopleListQueryString.TypeParameter)).Should().Be(query);
    }
}
