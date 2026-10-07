using Relio.Application.Portability;
using Relio.Web.Components.Pages;

namespace Relio.Web.Tests.Portability;

public sealed class UserDataPortabilityTextTests
{
    [Theory]
    [InlineData(UserDataPortabilityError.InvalidDocument)]
    [InlineData(UserDataPortabilityError.UnsupportedVersion)]
    [InlineData(UserDataPortabilityError.TooManyRows)]
    [InlineData(UserDataPortabilityError.DuplicateId)]
    [InlineData(UserDataPortabilityError.InvalidReference)]
    [InlineData(UserDataPortabilityError.InvalidValue)]
    [InlineData(UserDataPortabilityError.InvalidAuditDate)]
    [InlineData(UserDataPortabilityError.InvalidTimeZone)]
    [InlineData(UserDataPortabilityError.DestinationNotFresh)]
    public void Every_validation_code_has_calm_content_free_wording(UserDataPortabilityError error)
    {
        var message = UserDataPortabilityText.For([error]);

        message.Should().NotBeNullOrWhiteSpace();
        message.Should().NotContain(error.ToString());
        message.Should().NotContain("Ada");
    }
}
