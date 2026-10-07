using Relio.Domain;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class TagSuggestionsTests
{
    private static List<Tag> Tags(params string[] names) => [.. names.Select(name => new Tag { Name = name })];

    [Fact]
    public void Blank_text_lists_the_first_tags_by_name()
    {
        var available = Tags("Work", "Chess", "Mentor", "Family");

        var options = TagSuggestions.For("  ", available, [], max: 3);

        options.Select(o => o.Name).Should().Equal("Chess", "Family", "Mentor");
        options.Should().OnlyContain(o => !o.IsNew && o.Id != null);
    }

    [Fact]
    public void Typed_text_matches_anywhere_in_the_name_ignoring_case_and_lists_prefix_matches_first()
    {
        var available = Tags("Rock climbing", "Climbing", "Clim", "Chess");

        var options = TagSuggestions.For("CLIM", available, []);

        options.Where(o => !o.IsNew).Select(o => o.Name).Should().Equal("Clim", "Climbing", "Rock climbing");
    }

    [Fact]
    public void Tags_already_chosen_are_not_offered_again()
    {
        var chess = new Tag { Name = "Chess" };
        var available = new List<Tag> { chess, new() { Name = "Chessboard" } };

        var options = TagSuggestions.For("chess", available, [new TagSelection(chess.Id, "Chess")]);

        // Neither the chosen tag nor an offer to create "chess": the name is taken.
        options.Should().ContainSingle().Which.Name.Should().Be("Chessboard");
    }

    [Fact]
    public void A_create_option_is_not_offered_for_a_name_that_already_exists()
    {
        var available = Tags("Chess");

        TagSuggestions.For("chess", available, []).Should().ContainSingle().Which.IsNew.Should().BeFalse();
        TagSuggestions.For("  CHESS ", available, []).Should().NotContain(o => o.IsNew);
    }

    [Fact]
    public void A_new_name_is_offered_for_creation_after_the_matches_with_the_typed_casing()
    {
        var options = TagSuggestions.For("  Rock   climbing ", Tags("Rock", "Climbing"), []);

        options.Should().ContainSingle(o => o.IsNew);
        options.Last().Should().Be(new TagOption(null, "Rock climbing", IsNew: true));
    }

    [Fact]
    public void A_name_chosen_as_a_new_tag_already_is_not_offered_again()
    {
        var selected = new List<TagSelection> { new(null, "Climbing") };

        TagSuggestions.For("climbing", [], selected).Should().BeEmpty();
    }

    [Fact]
    public void Blank_text_and_names_over_50_characters_offer_nothing_to_create()
    {
        TagSuggestions.For(null, [], []).Should().BeEmpty();
        TagSuggestions.For(new string('a', Tag.NameMaxLength), [], []).Should().ContainSingle(o => o.IsNew);
        TagSuggestions.For(new string('a', Tag.NameMaxLength + 1), [], []).Should().BeEmpty();
    }

    [Fact]
    public void At_most_the_given_number_of_existing_tags_are_listed()
    {
        var available = Tags(Enumerable.Range(0, 30).Select(i => $"Tag {i:00}").ToArray());

        TagSuggestions.For("tag", available, []).Count(o => !o.IsNew).Should().Be(TagSuggestions.DefaultMax);
    }
}
