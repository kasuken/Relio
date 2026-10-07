using Relio.Application.People;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PersonMergeTextTests
{
    public static TheoryData<MergeField> Fields() => new(Enum.GetValues<MergeField>());

    [Theory]
    [MemberData(nameof(Fields))]
    public void Every_field_has_a_label(MergeField field)
    {
        PersonMergeText.FieldLabel(field).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_unknown_field_has_no_label()
    {
        Action act = () => PersonMergeText.FieldLabel((MergeField)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Field_labels_match_the_profile_and_form_words()
    {
        Enum.GetValues<MergeField>().Select(PersonMergeText.FieldLabel).Should().Equal(
            "Name", "Nickname", "Relationship", "Birthday", "How you met", "Details", "Status");
    }

    [Fact]
    public void Problems_explain_the_merge_limits()
    {
        PersonMergeText.Problem(PersonValidationError.TooManyContactMethods).Should().Contain("more than 20 contact methods").And.Contain("merge");
        PersonMergeText.Problem(PersonValidationError.TooManyTags).Should().Contain("more than 20 tags").And.Contain("merge");
        PersonMergeText.Problem(PersonValidationError.HowWeMetTooLong).Should().Contain("too long").And.Contain("Keep one of them");
        PersonMergeText.Problem(PersonValidationError.DetailsTooLong).Should().Be(PersonMergeText.Problem(PersonValidationError.HowWeMetTooLong));
    }

    [Fact]
    public void Every_other_validation_error_falls_back_to_the_person_forms_message()
    {
        foreach (var error in Enum.GetValues<PersonValidationError>()
                     .Except([
                         PersonValidationError.TooManyContactMethods,
                         PersonValidationError.TooManyTags,
                         PersonValidationError.HowWeMetTooLong,
                         PersonValidationError.DetailsTooLong,
                     ]))
        {
            PersonMergeText.Problem(error).Should().Be(PersonFormMessages.For(error).Message, error.ToString());
        }
    }

    [Fact]
    public void The_snackbar_text_never_contains_a_name()
    {
        PersonMergeText.Merged.Should().Be("Profiles merged");
        PersonMergeText.Gone.Should().NotContain("'s");
    }

    [Fact]
    public void The_confirmation_names_both_people_and_says_it_cannot_be_undone()
    {
        PersonMergeText.ConfirmTitle("Jon Smith", "John Smith").Should().Be("Merge Jon Smith into John Smith?");
        PersonMergeText.ConfirmMessage("Jon Smith", "John Smith").Should()
            .Be("Jon Smith's profile will be removed. Everything recorded about them moves to John Smith. This can't be undone.");
        PersonMergeText.ConfirmLabel.Should().Be("Merge profiles");
    }

    [Fact]
    public void The_repeated_note_is_singular_and_plural()
    {
        PersonMergeText.RepeatedCombined(1).Should().Be("1 repeated detail combined");
        PersonMergeText.RepeatedCombined(3).Should().Be("3 repeated details combined");
    }
}
