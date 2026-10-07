using Relio.Application.Notes;
using Relio.Domain;

namespace Relio.Application.Tests.Notes;

public class NoteRulesTests
{
    [Theory]
    [InlineData("  A note to keep.  ", "A note to keep.")]
    [InlineData("\n\t  ", "")]
    [InlineData(null, "")]
    public void NormalizeText_trims_input(string? text, string expected)
    {
        NoteRules.NormalizeText(text).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public void Validate_requires_non_whitespace_text(string? text)
    {
        NoteRules.Validate(text).Should().Equal(NoteValidationError.TextRequired);
    }

    [Fact]
    public void Validate_accepts_the_maximum_length_after_trimming()
    {
        var text = $"  {new string('x', Note.TextMaxLength)}  ";

        NoteRules.Validate(text).Should().BeEmpty();
        NoteRules.NormalizeText(text).Should().HaveLength(Note.TextMaxLength);
    }

    [Fact]
    public void Validate_rejects_text_longer_than_the_limit_after_trimming()
    {
        var text = new string('x', Note.TextMaxLength + 1);

        NoteRules.Validate(text).Should().Equal(NoteValidationError.TextTooLong);
    }

    [Fact]
    public void NoteValidationException_message_contains_codes_not_submitted_note_text()
    {
        const string privateText = "private relationship detail";

        var exception = new NoteValidationException([NoteValidationError.TextRequired]);

        exception.Errors.Should().Equal(NoteValidationError.TextRequired);
        exception.Message.Should().Contain(nameof(NoteValidationError.TextRequired));
        exception.Message.Should().NotContain(privateText);
    }
}
