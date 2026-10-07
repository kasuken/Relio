using Relio.Application.People;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PossibleDuplicateTextTests
{
    public static TheoryData<PossibleDuplicateReason> Reasons =>
        new(Enum.GetValues<PossibleDuplicateReason>());

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Every_reason_has_wording(PossibleDuplicateReason reason)
    {
        PossibleDuplicateText.Reason(reason).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_unknown_reason_is_refused()
    {
        var act = () => PossibleDuplicateText.Reason((PossibleDuplicateReason)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Describe_joins_reasons_and_marks_archived()
    {
        var active = new PossibleDuplicate(
            Guid.NewGuid(), "John", "Smith", false, [PossibleDuplicateReason.SimilarName, PossibleDuplicateReason.SameEmail]);
        var archived = new PossibleDuplicate(
            Guid.NewGuid(), "Ada", null, true, [PossibleDuplicateReason.SamePhone]);

        PossibleDuplicateText.Describe(active).Should().Be("Similar name · Same email address");
        PossibleDuplicateText.Describe(archived).Should().Be("Same phone number · Archived");
    }

    [Fact]
    public void Intro_reads_naturally_for_one_and_many()
    {
        PossibleDuplicateText.Intro(1).Should().Be("This profile looks similar. Open it to check, or save anyway.");
        PossibleDuplicateText.Intro(3).Should().Be("These profiles look similar. Open them to check, or save anyway.");
    }

    [Fact]
    public void The_wording_never_shouts_or_blames()
    {
        PossibleDuplicateText.Title.Should().NotContain("!");
        PossibleDuplicateText.Intro(2).Should().NotContain("!");
    }
}
