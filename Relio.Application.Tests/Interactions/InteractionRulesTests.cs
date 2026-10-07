using Relio.Application.Interactions;
using Relio.Domain;

namespace Relio.Application.Tests.Interactions;

public class InteractionRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public void Validate_accepts_a_valid_interaction_and_trims_its_description()
    {
        var personId = Guid.NewGuid();

        var errors = InteractionRules.Validate(
            Today,
            InteractionKind.Call,
            "  Talked about the weekend.  ",
            [personId],
            Today,
            personId);

        errors.Should().BeEmpty();
        InteractionRules.NormalizeDescription("  Talked about the weekend.  ").Should().Be("Talked about the weekend.");
    }

    [Fact]
    public void Validate_reports_date_kind_and_description_errors_without_echoing_input()
    {
        var errors = InteractionRules.Validate(
            Today.AddDays(1),
            (InteractionKind)999,
            new string('x', Interaction.DescriptionMaxLength + 1),
            [Guid.NewGuid()],
            Today);

        errors.Should().Equal(
            InteractionValidationError.DateInFuture,
            InteractionValidationError.KindInvalid,
            InteractionValidationError.DescriptionTooLong);
    }

    [Fact]
    public void Validate_requires_a_date_and_a_nonblank_description()
    {
        var errors = InteractionRules.Validate(
            default,
            InteractionKind.Other,
            " \r\n\t ",
            [Guid.NewGuid()],
            Today);

        errors.Should().Equal(
            InteractionValidationError.DateRequired,
            InteractionValidationError.DescriptionRequired);
    }

    [Fact]
    public void Validate_requires_the_profile_person_and_unique_participants()
    {
        var profileId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var errors = InteractionRules.Validate(
            Today,
            InteractionKind.Meeting,
            "Met for lunch.",
            [otherId, otherId],
            Today,
            profileId);

        errors.Should().Equal(
            InteractionValidationError.DuplicateParticipant,
            InteractionValidationError.ProfileParticipantRequired);
    }

    [Fact]
    public void Validate_rejects_empty_and_excessive_participant_lists()
    {
        var ids = Enumerable.Range(0, InteractionRules.MaxParticipants + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        ids[0] = Guid.Empty;

        var errors = InteractionRules.Validate(
            Today,
            InteractionKind.Event,
            "A shared event.",
            ids,
            Today);

        errors.Should().Equal(
            InteractionValidationError.TooManyParticipants,
            InteractionValidationError.ParticipantIdInvalid);
    }

    [Fact]
    public void Validate_does_not_truncate_the_supported_maximum()
    {
        var ids = Enumerable.Range(0, InteractionRules.MaxParticipants).Select(_ => Guid.NewGuid()).ToArray();

        InteractionRules.Validate(Today, InteractionKind.Message, "A group message.", ids, Today)
            .Should().BeEmpty();
    }
}
