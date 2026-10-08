using Relio.Application.DifficultMoments;
using Relio.Domain;

namespace Relio.Application.Tests.DifficultMoments;

public class DifficultMomentRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    [Fact]
    public void Validate_accepts_a_valid_difficult_moment_and_normalizes_text()
    {
        var errors = DifficultMomentRules.Validate(
            Today,
            "  We had a disagreement about finances.  ",
            "  Unexpected expense.  ",
            "  Agreed on a budget plan.  ",
            "  Talk earlier before stress builds up.  ",
            DifficultMomentStatus.Resolved,
            Today,
            null,
            null,
            Today);

        errors.Should().BeEmpty();
        DifficultMomentRules.NormalizeDescription("  We had a disagreement.  ").Should().Be("We had a disagreement.");
        DifficultMomentRules.NormalizeOptionalText("  Unexpected expense.  ").Should().Be("Unexpected expense.");
        DifficultMomentRules.NormalizeOptionalText("   ").Should().BeNull();
        DifficultMomentRules.NormalizeOptionalText(null).Should().BeNull();
    }

    [Fact]
    public void Validate_requires_date_and_description()
    {
        var errors = DifficultMomentRules.Validate(
            default,
            "   ",
            null,
            null,
            null,
            DifficultMomentStatus.Open,
            null,
            null,
            null,
            Today);

        errors.Should().Equal(
            DifficultMomentValidationError.DateRequired,
            DifficultMomentValidationError.DescriptionRequired);
    }

    [Fact]
    public void Validate_reports_future_date_and_length_violations()
    {
        var errors = DifficultMomentRules.Validate(
            Today.AddDays(1),
            new string('a', DifficultMoment.DescriptionMaxLength + 1),
            new string('b', DifficultMoment.TriggerMaxLength + 1),
            new string('c', DifficultMoment.ResolutionMaxLength + 1),
            new string('d', DifficultMoment.LessonsLearnedMaxLength + 1),
            (DifficultMomentStatus)999,
            Today.AddDays(2),
            null,
            null,
            Today);

        errors.Should().Equal(
            DifficultMomentValidationError.DateInFuture,
            DifficultMomentValidationError.DescriptionTooLong,
            DifficultMomentValidationError.TriggerTooLong,
            DifficultMomentValidationError.ResolutionTooLong,
            DifficultMomentValidationError.LessonsLearnedTooLong,
            DifficultMomentValidationError.StatusInvalid,
            DifficultMomentValidationError.ResolvedDateInFuture);
    }

    [Fact]
    public void Validate_rejects_resolved_date_before_occurred_date()
    {
        var errors = DifficultMomentRules.Validate(
            Today,
            "Disagreement",
            null,
            null,
            null,
            DifficultMomentStatus.Resolved,
            Today.AddDays(-1),
            null,
            null,
            Today);

        errors.Should().Equal(DifficultMomentValidationError.ResolvedDateBeforeOccurredOn);
    }

    [Fact]
    public void Validate_rejects_self_referencing_recurrence()
    {
        var momentId = Guid.NewGuid();
        var errors = DifficultMomentRules.Validate(
            Today,
            "Disagreement",
            null,
            null,
            null,
            DifficultMomentStatus.Recurring,
            null,
            momentId,
            momentId,
            Today);

        errors.Should().Equal(DifficultMomentValidationError.RecurrenceSelfReference);
    }
}
