using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Tests.People;

public class LabelNameRulesTests
{
    [Theory]
    [InlineData("  Book   club ", "Book club")]
    [InlineData("a\t\nb", "a b")]
    [InlineData("CamelCase", "CamelCase")]
    public void Normalize_trims_and_collapses_whitespace_keeping_case(string name, string expected)
    {
        LabelNameRules.Normalize(name).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_requires_a_name(string? name)
    {
        LabelNameRules.Validate(LabelNameRules.Normalize(name), 50).Should().Be(LabelValidationError.NameRequired);
    }

    [Fact]
    public void Validate_measures_length_after_normalizing()
    {
        var padded = "  " + new string('x', 50) + "   ";
        var tooLong = new string('x', 51);

        LabelNameRules.Validate(LabelNameRules.Normalize(padded), 50).Should().BeNull();
        LabelNameRules.Validate(LabelNameRules.Normalize(tooLong), 50).Should().Be(LabelValidationError.NameTooLong);
    }

    [Fact]
    public void Comparer_treats_case_variants_as_equal_but_not_accents()
    {
        LabelNameRules.Comparer.Equals("Friend", "fRIEND").Should().BeTrue();
        LabelNameRules.Comparer.Equals("Cafe", "Café").Should().BeFalse();
    }

    [Fact]
    public void Name_limits_match_for_tags_and_relationship_types()
    {
        Tag.NameMaxLength.Should().Be(RelationshipType.NameMaxLength).And.Be(50);
    }

    [Fact]
    public void LabelValidationException_message_names_the_code_but_never_the_name()
    {
        var exception = new LabelValidationException(LabelValidationError.NameTaken);

        exception.Error.Should().Be(LabelValidationError.NameTaken);
        exception.Message.Should().Contain("NameTaken");
        exception.InnerException.Should().BeNull();
    }
}
