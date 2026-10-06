using Relio.Application.Paging;
using Relio.Application.People;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PeopleListTextTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Theory]
    [InlineData(null, "Not contacted yet")]
    [InlineData("2026-10-06", "Last contacted today")]
    [InlineData("2026-10-05", "Last contacted yesterday")]
    [InlineData("2026-10-04", "Last contacted 2 days ago")]
    [InlineData("2026-09-24", "Last contacted 12 days ago")]
    [InlineData("2026-09-23", "Last contacted 13 days ago")]
    [InlineData("2026-09-22", "Last contacted 22 September")]
    [InlineData("2025-03-03", "Last contacted 3 March 2025")]
    [InlineData("2026-10-09", "Last contacted 9 October")]
    public void LastContacted_reads_naturally(string? date, string expected)
    {
        DateOnly? parsed = date is null ? null : DateOnly.Parse(date);

        PeopleListText.LastContacted(parsed, Today).Should().Be(expected);
    }

    [Theory]
    [InlineData(PeopleSort.Name, "Name")]
    [InlineData(PeopleSort.RecentlyAdded, "Recently added")]
    [InlineData(PeopleSort.LastContacted, "Last contacted")]
    public void SortLabel_is_sentence_case(PeopleSort sort, string expected)
    {
        PeopleListText.SortLabel(sort).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 0, false, "1 person")]
    [InlineData(12, 3, false, "12 people")]
    [InlineData(1234, 0, false, "1,234 people")]
    [InlineData(5, 2, true, "7 people, 2 archived")]
    [InlineData(1, 0, true, "1 person, none archived")]
    [InlineData(0, 1, true, "1 person, 1 archived")]
    [InlineData(3, 0, true, "3 people, none archived")]
    public void Count_describes_the_list(int active, int archived, bool includeArchived, string expected)
    {
        var result = new PeopleListResult(new PagedResult<PersonListItem>([], 1, 50, 0), active, archived);

        PeopleListText.Count(result, includeArchived).Should().Be(expected);
    }
}
