using Relio.Domain;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class ContactMethodDisplayTests
{
    public static TheoryData<ContactMethodKind> AllKinds
    {
        get
        {
            var data = new TheoryData<ContactMethodKind>();
            foreach (var kind in Enum.GetValues<ContactMethodKind>())
            {
                data.Add(kind);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_kind_has_a_name_a_value_label_and_an_icon(ContactMethodKind kind)
    {
        ContactMethodDisplay.KindName(kind).Should().NotBeNullOrWhiteSpace();
        ContactMethodDisplay.ValueLabel(kind).Should().NotBeNullOrWhiteSpace();
        ContactMethodDisplay.Icon(kind).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Kinds_are_named_as_a_person_would_say_them()
    {
        Enum.GetValues<ContactMethodKind>().Select(ContactMethodDisplay.KindName)
            .Should().Equal("Email", "Phone", "Address", "Social", "Other");
        Enum.GetValues<ContactMethodKind>().Select(ContactMethodDisplay.ValueLabel)
            .Should().Equal("Email address", "Phone number", "Address", "Handle or link", "Details");
    }

    [Fact]
    public void Each_kind_has_its_own_icon()
    {
        Enum.GetValues<ContactMethodKind>().Select(ContactMethodDisplay.Icon).Distinct().Should().HaveCount(5);
    }

    [Theory]
    [InlineData(ContactMethodKind.Email, null, "Email")]
    [InlineData(ContactMethodKind.Email, "", "Email")]
    [InlineData(ContactMethodKind.Email, "   ", "Email")]
    [InlineData(ContactMethodKind.Phone, "Mobile", "Phone · Mobile")]
    [InlineData(ContactMethodKind.Social, "  Instagram ", "Social · Instagram")]
    public void The_heading_is_the_kind_and_the_label_when_there_is_one(ContactMethodKind kind, string? label, string expected)
    {
        ContactMethodDisplay.Heading(kind, label).Should().Be(expected);
    }

    [Fact]
    public void An_undefined_kind_is_refused_rather_than_shown_blank()
    {
        var kind = (ContactMethodKind)99;

        FluentActions.Invoking(() => ContactMethodDisplay.KindName(kind)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ContactMethodDisplay.ValueLabel(kind)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ContactMethodDisplay.Icon(kind)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
