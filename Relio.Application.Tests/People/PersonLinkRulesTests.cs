using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Tests.People;

public sealed class PersonLinkRulesTests
{
    private static readonly Guid Viewed = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void A_valid_request_has_no_errors()
    {
        PersonLinkRules.Validate(Viewed, Other, PersonLinkKind.Friend).Should().BeEmpty();
    }

    [Fact]
    public void An_empty_related_person_is_reported_as_missing()
    {
        PersonLinkRules.Validate(Viewed, Guid.Empty, PersonLinkKind.Friend)
            .Should().Equal(PersonLinkValidationError.PersonRequired);
    }

    [Fact]
    public void Linking_a_person_to_themselves_is_refused()
    {
        PersonLinkRules.Validate(Viewed, Viewed, PersonLinkKind.Friend)
            .Should().Equal(PersonLinkValidationError.SelfLink);
    }

    [Fact]
    public void A_missing_kind_is_reported()
    {
        PersonLinkRules.Validate(Viewed, Other, null)
            .Should().Equal(PersonLinkValidationError.KindRequired);
    }

    [Fact]
    public void An_undefined_kind_is_reported()
    {
        PersonLinkRules.Validate(Viewed, Other, (PersonLinkKind)999)
            .Should().Equal(PersonLinkValidationError.KindInvalid);
    }

    [Fact]
    public void Several_problems_are_reported_together()
    {
        PersonLinkRules.Validate(Viewed, Guid.Empty, null)
            .Should().Equal(PersonLinkValidationError.PersonRequired, PersonLinkValidationError.KindRequired);
    }

    [Fact]
    public void ValidateKind_checks_only_the_kind()
    {
        PersonLinkRules.ValidateKind(PersonLinkKind.Parent).Should().BeEmpty();
        PersonLinkRules.ValidateKind(null).Should().Equal(PersonLinkValidationError.KindRequired);
        PersonLinkRules.ValidateKind((PersonLinkKind)(-1)).Should().Equal(PersonLinkValidationError.KindInvalid);
    }

    [Fact]
    public void The_exception_message_lists_codes_only()
    {
        var exception = new PersonLinkValidationException(
            [PersonLinkValidationError.SelfLink, PersonLinkValidationError.KindRequired]);

        exception.Message.Should().Be("Person link validation failed with errors: SelfLink, KindRequired");
        exception.Errors.Should().HaveCount(2);
    }
}
