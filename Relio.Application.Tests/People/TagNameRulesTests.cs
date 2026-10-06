using Relio.Application.People;

namespace Relio.Application.Tests.People;

public class TagNameRulesTests
{
    [Theory]
    [InlineData("  Rock   climbing ", "Rock climbing")]
    [InlineData("Chess", "Chess")]
    [InlineData("a\t\nb", "a b")]
    public void Normalize_trims_and_collapses_whitespace(string name, string expected)
    {
        TagNameRules.Normalize(name).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t\n ")]
    public void Normalize_turns_blank_into_null(string? name)
    {
        TagNameRules.Normalize(name).Should().BeNull();
    }

    [Fact]
    public void Normalize_keeps_the_casing_the_user_typed()
    {
        TagNameRules.Normalize("  CamelCase Tag").Should().Be("CamelCase Tag");
    }

    [Theory]
    [InlineData("Chess", "chess")]
    [InlineData("CLIMBING", "Climbing")]
    [InlineData("Élan", "élan")]
    public void Comparer_matches_names_case_insensitively(string left, string right)
    {
        TagNameRules.Comparer.Equals(left, right).Should().BeTrue();
        TagNameRules.Comparer.GetHashCode(left).Should().Be(TagNameRules.Comparer.GetHashCode(right));
    }

    [Fact]
    public void Comparer_keeps_accented_names_distinct()
    {
        TagNameRules.Comparer.Equals("Cafe", "Café").Should().BeFalse();
    }
}
