using Relio.Application.People;

namespace Relio.Application.Tests.People;

/// <summary>The fuzzy name rules behind duplicate detection (#27). Inputs are already normalized.</summary>
public class NameSimilarityTests
{
    [Theory]
    [InlineData("jon", "john", 1)]
    [InlineData("jhon", "john", 1)]
    [InlineData("john", "john", 0)]
    [InlineData("", "ab", 2)]
    [InlineData("kitten", "sitting", 3)]
    public void Distance_counts_insertions_deletions_substitutions_and_transpositions(string a, string b, int expected)
    {
        NameSimilarity.Distance(a, b, 3).Should().Be(expected);
    }

    [Fact]
    public void Distance_stops_early_above_the_limit()
    {
        NameSimilarity.Distance("kitten", "sitting", 2).Should().Be(3);
        NameSimilarity.Distance("a", "abcdef", 2).Should().Be(3);
        NameSimilarity.Distance("abcdef", "uvwxyz", 1).Should().Be(2);
        NameSimilarity.Distance("", "abc", 1).Should().Be(2);
    }

    [Theory]
    [InlineData("jon", "john")]
    [InlineData("jhon", "john")]
    [InlineData("ann", "anna")]
    [InlineData("sara", "sarah")]
    [InlineData("alex", "alexander")]
    [InlineData("sam", "samantha")]
    [InlineData("kathrine", "katherine")]
    [InlineData("christophr", "christopher")]
    [InlineData("hanna", "anna")]
    [InlineData("john", "john paul")]
    [InlineData("ada", "ada")]
    public void FirstNamesClose_accepts_typos_and_short_forms(string a, string b)
    {
        NameSimilarity.FirstNamesClose(a, b).Should().BeTrue();
        NameSimilarity.FirstNamesClose(b, a).Should().BeTrue();
    }

    [Theory]
    [InlineData("mark", "mary")]
    [InlineData("jon", "jan")]
    [InlineData("dan", "don")]
    [InlineData("al", "ali")]
    [InlineData("ed", "ted")]
    [InlineData("marina", "martin")]
    [InlineData("eric", "erik")]
    [InlineData("anna", "maria")]
    public void FirstNamesClose_rejects_different_names(string a, string b)
    {
        // "eric"/"erik" is a documented miss: one substitution in a short name is usually a different name.
        NameSimilarity.FirstNamesClose(a, b).Should().BeFalse();
        NameSimilarity.FirstNamesClose(b, a).Should().BeFalse();
    }

    [Theory]
    [InlineData("smith", "smith", true)]
    [InlineData("smith", "smyth", true)]
    [InlineData("johnson", "jonson", true)]
    [InlineData("hall", "hill", false)]
    [InlineData("rossi", "rosi", false)]
    [InlineData("smith", "jones", false)]
    [InlineData("rutherford", "rutherfurd", true)]
    [InlineData("smith", "smithson", false)]
    public void LastNamesClose_needs_five_letters_and_a_small_typo(string a, string b, bool expected)
    {
        NameSimilarity.LastNamesClose(a, b).Should().Be(expected);
        NameSimilarity.LastNamesClose(b, a).Should().Be(expected);
    }
}
