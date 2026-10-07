using Relio.Application.Notes;
using Relio.Web.Components.Notes;

namespace Relio.Web.Tests.Notes;

public class NoteFormMessagesTests
{
    public static TheoryData<NoteValidationError> AllErrors
    {
        get
        {
            var data = new TheoryData<NoteValidationError>();
            foreach (var error in Enum.GetValues<NoteValidationError>())
            {
                data.Add(error);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllErrors))]
    public void Every_validation_error_has_a_message(NoteValidationError error)
    {
        var message = NoteFormMessages.For(error);

        message.Should().NotBeNullOrWhiteSpace();
        message.Should().EndWith(".");
        message.Should().NotContain("!");
        message.ToLowerInvariant().Should().NotContain("sorry");
    }

    [Theory]
    [InlineData(NoteValidationError.TextRequired, "Enter a note.")]
    [InlineData(NoteValidationError.TextTooLong, "A note can be up to 10,000 characters.")]
    public void Validation_messages_use_the_note_field_wording(NoteValidationError error, string expected)
    {
        NoteFormMessages.For(error).Should().Be(expected);
    }

    [Fact]
    public void An_unknown_error_is_a_programming_mistake_not_a_silent_blank()
    {
        var act = () => NoteFormMessages.For((NoteValidationError)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
