using Relio.Application.DifficultMoments;
using Relio.Web.Components.DifficultMoments;

namespace Relio.Web.Tests.DifficultMoments;

public class DifficultMomentFormMessagesTests
{
    public static TheoryData<DifficultMomentValidationError> AllErrors
    {
        get
        {
            var data = new TheoryData<DifficultMomentValidationError>();
            foreach (var error in Enum.GetValues<DifficultMomentValidationError>())
            {
                data.Add(error);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllErrors))]
    public void Every_validation_error_has_a_message(DifficultMomentValidationError error)
    {
        var message = DifficultMomentFormMessages.Message(error);

        message.Should().NotBeNullOrWhiteSpace();
        message.Should().EndWith(".");
        message.Should().NotContain("!");
        message.ToLowerInvariant().Should().NotContain("sorry");
    }

    [Fact]
    public void An_unknown_error_throws_ArgumentOutOfRangeException()
    {
        var act = () => DifficultMomentFormMessages.Message((DifficultMomentValidationError)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
