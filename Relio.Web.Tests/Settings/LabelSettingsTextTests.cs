using Relio.Application.People;
using Relio.Web.Components.Settings;

namespace Relio.Web.Tests.Settings;

public class LabelSettingsTextTests
{
    [Theory]
    [InlineData(0, "No one yet")]
    [InlineData(1, "1 person")]
    [InlineData(2, "2 people")]
    [InlineData(1000, "1,000 people")]
    public void UsedBy_reads_naturally(int people, string expected)
    {
        LabelSettingsText.UsedBy(people).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 person has this relationship type.")]
    [InlineData(2, "2 people have this relationship type.")]
    public void TypeInUse_agrees_with_the_number(int people, string expected)
    {
        LabelSettingsText.TypeInUse(people).Should().Be(expected);
    }

    [Fact]
    public void Every_error_has_a_message_for_every_kind()
    {
        foreach (var kind in Enum.GetValues<LabelKind>())
        {
            foreach (var error in Enum.GetValues<LabelValidationError>())
            {
                LabelSettingsText.Message(error, kind).Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public void A_taken_name_names_the_kind_of_label()
    {
        LabelSettingsText.Message(LabelValidationError.NameTaken, LabelKind.Tag).Should().Contain("tag");
        LabelSettingsText.Message(LabelValidationError.NameTaken, LabelKind.RelationshipType).Should().Contain("relationship type");
    }

    [Fact]
    public void TagRemoval_explains_what_happens()
    {
        LabelSettingsText.TagRemoval(0).Should().Be("No one has this tag.");
        LabelSettingsText.TagRemoval(1).Should().Contain("1 person has this tag").And.Contain("takes it off them");
        LabelSettingsText.TagRemoval(3).Should().Contain("3 people have this tag").And.Contain("nothing else about them changes");
    }

    [Fact]
    public void The_name_limit_in_the_message_matches_the_limit_of_both_labels()
    {
        LabelSettingsText.Message(LabelValidationError.NameTooLong, LabelKind.Tag)
            .Should().Contain(Relio.Domain.Tag.NameMaxLength.ToString())
            .And.Contain(Relio.Domain.RelationshipType.NameMaxLength.ToString());
    }
}
