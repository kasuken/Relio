using Relio.Domain;

namespace Relio.Application.Tests.People;

public sealed class PersonLinkTests
{
    private static readonly Guid Low = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid High = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public static TheoryData<PersonLinkKind> AllKinds()
    {
        var data = new TheoryData<PersonLinkKind>();
        foreach (var kind in Enum.GetValues<PersonLinkKind>())
        {
            data.Add(kind);
        }

        return data;
    }

    [Fact]
    public void Reciprocal_is_an_involution_for_every_kind()
    {
        foreach (var kind in PersonLinkKinds.All)
        {
            PersonLinkKinds.Reciprocal(PersonLinkKinds.Reciprocal(kind)).Should().Be(kind);
        }
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Directional_kinds_swap_and_symmetric_kinds_map_to_themselves(PersonLinkKind kind)
    {
        var expected = kind switch
        {
            PersonLinkKind.Parent => PersonLinkKind.Child,
            PersonLinkKind.Child => PersonLinkKind.Parent,
            PersonLinkKind.Grandparent => PersonLinkKind.Grandchild,
            PersonLinkKind.Grandchild => PersonLinkKind.Grandparent,
            PersonLinkKind.Manager => PersonLinkKind.DirectReport,
            PersonLinkKind.DirectReport => PersonLinkKind.Manager,
            _ => kind,
        };

        PersonLinkKinds.Reciprocal(kind).Should().Be(expected);
        PersonLinkKinds.IsSymmetric(kind).Should().Be(expected == kind);
    }

    [Fact]
    public void Reciprocal_rejects_an_undefined_kind()
    {
        var act = () => PersonLinkKinds.Reciprocal((PersonLinkKind)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_kinds_are_listed_in_declaration_order()
    {
        PersonLinkKinds.All.Should().Equal(Enum.GetValues<PersonLinkKind>());
        PersonLinkKinds.All.Should().HaveCount(14);
    }

    [Fact]
    public void SetEnds_stores_the_lower_guid_first_with_the_kind_as_seen_from_it()
    {
        var fromLow = new PersonLink();
        fromLow.SetEnds(Low, High, PersonLinkKind.Parent);

        var fromHigh = new PersonLink();
        fromHigh.SetEnds(High, Low, PersonLinkKind.Child);

        foreach (var link in new[] { fromLow, fromHigh })
        {
            link.PersonId.Should().Be(Low);
            link.RelatedPersonId.Should().Be(High);
            link.Kind.Should().Be(PersonLinkKind.Parent);
        }
    }

    [Fact]
    public void RoleOfOther_is_the_kind_from_one_end_and_its_reciprocal_from_the_other()
    {
        var link = new PersonLink();
        link.SetEnds(Low, High, PersonLinkKind.Parent);

        link.RoleOfOther(Low).Should().Be(PersonLinkKind.Parent);
        link.RoleOfOther(High).Should().Be(PersonLinkKind.Child);
        link.OtherPersonId(Low).Should().Be(High);
        link.OtherPersonId(High).Should().Be(Low);
        link.Involves(Low).Should().BeTrue();
        link.Involves(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void OtherPersonId_and_RoleOfOther_reject_a_person_who_is_not_an_end()
    {
        var link = new PersonLink();
        link.SetEnds(Low, High, PersonLinkKind.Friend);
        var stranger = Guid.NewGuid();

        var other = () => link.OtherPersonId(stranger);
        var role = () => link.RoleOfOther(stranger);

        other.Should().Throw<ArgumentException>();
        role.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetEnds_rejects_the_same_person_twice()
    {
        var link = new PersonLink();

        var act = () => link.SetEnds(Low, Low, PersonLinkKind.Friend);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_kind_name_fits_the_column(PersonLinkKind kind)
    {
        kind.ToString().Length.Should().BeLessThanOrEqualTo(PersonLink.KindMaxLength);
    }
}
