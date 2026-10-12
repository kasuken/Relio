using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PersonLinkTextTests
{
    [Fact]
    public void Every_kind_has_a_distinct_non_empty_role()
    {
        var roles = PersonLinkKinds.All.Select(PersonLinkText.Role).ToArray();

        roles.Should().OnlyContain(role => !string.IsNullOrWhiteSpace(role));
        roles.Should().OnlyHaveUniqueItems();
        roles.Should().HaveCount(Enum.GetValues<PersonLinkKind>().Length);
    }

    [Fact]
    public void An_unknown_kind_is_a_programming_error()
    {
        var act = () => PersonLinkText.Role((PersonLinkKind)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_kind_list_covers_the_whole_enum()
    {
        PersonLinkKinds.All.Should().BeEquivalentTo(Enum.GetValues<PersonLinkKind>());
    }

    [Fact]
    public void Role_names_read_naturally_in_both_directions()
    {
        PersonLinkText.Role(PersonLinkKind.DirectReport).Should().Be("Direct report");
        PersonLinkText.Role(PersonLinkKind.FormerPartner).Should().Be("Former partner");
        PersonLinkText.Role(PersonLinkKinds.Reciprocal(PersonLinkKind.Parent)).Should().Be("Child");
    }

    [Fact]
    public void The_words_name_the_first_name_only_where_they_must_and_never_an_id()
    {
        PersonLinkText.KindLabel("Anna").Should().Be("What are they to Anna?");
        PersonLinkText.Empty("Anna").Should().Contain("Anna").And.Contain("No connections yet");
        PersonLinkText.EditLabel("Marco Rossi").Should().Be("Edit connection with Marco Rossi");
        PersonLinkText.RemoveLabel("Marco Rossi").Should().Be("Remove connection with Marco Rossi");
        new[]
        {
            PersonLinkText.Added, PersonLinkText.Updated, PersonLinkText.Removed, PersonLinkText.Gone,
            PersonLinkText.PersonGone, PersonLinkText.RemoveTitle, PersonLinkText.RemoveMessage,
        }.Should().OnlyContain(text => !text.Contains("{") && !text.Contains("!"));
    }
}

public class PersonLinkFormMessagesTests
{
    [Fact]
    public void Every_validation_code_has_a_message()
    {
        foreach (var error in Enum.GetValues<PersonLinkValidationError>())
        {
            PersonLinkFormMessages.Message(error).Should().NotBeNullOrWhiteSpace($"{error} needs words");
        }
    }

    [Fact]
    public void Every_code_belongs_to_the_person_the_kind_or_the_general_alert()
    {
        PersonLinkFormMessages.BelongsToPerson(PersonLinkValidationError.PersonRequired).Should().BeTrue();
        PersonLinkFormMessages.BelongsToPerson(PersonLinkValidationError.SelfLink).Should().BeTrue();
        PersonLinkFormMessages.BelongsToKind(PersonLinkValidationError.KindRequired).Should().BeTrue();
        PersonLinkFormMessages.BelongsToKind(PersonLinkValidationError.KindInvalid).Should().BeTrue();
        PersonLinkFormMessages.BelongsToPerson(PersonLinkValidationError.AlreadyLinked).Should().BeFalse();
        PersonLinkFormMessages.BelongsToKind(PersonLinkValidationError.ArchivedPersonNotAllowed).Should().BeFalse();
    }

    [Fact]
    public void An_unknown_code_is_a_programming_error()
    {
        var act = () => PersonLinkFormMessages.Message((PersonLinkValidationError)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
