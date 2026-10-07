using Relio.Application.Interactions;

namespace Relio.Web.Components.People;

/// <summary>Maps interaction validation codes to calm, field-level UI text.</summary>
public static class InteractionFormMessages
{
    /// <summary>Returns a user-facing message for every interaction validation code.</summary>
    public static string Message(InteractionValidationError error) => error switch
    {
        InteractionValidationError.DateRequired => "Choose the date this happened.",
        InteractionValidationError.DateInFuture => "The date can't be in the future.",
        InteractionValidationError.KindInvalid => "Choose an interaction type.",
        InteractionValidationError.DescriptionRequired => "Add a short description.",
        InteractionValidationError.DescriptionTooLong => "Keep the description to 10,000 characters or fewer.",
        InteractionValidationError.ParticipantsRequired => "Choose at least one person.",
        InteractionValidationError.ProfileParticipantRequired => "Include this person in the interaction.",
        InteractionValidationError.TooManyParticipants => "An interaction can include up to 20 people.",
        InteractionValidationError.ParticipantIdInvalid => "Refresh the form and choose the people again.",
        InteractionValidationError.DuplicateParticipant => "Choose each person only once.",
        InteractionValidationError.ArchivedParticipantNotAllowed => "Restore an archived profile before adding it.",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown interaction validation code."),
    };

    /// <summary>Returns the complete list of UI messages for an exception.</summary>
    public static IReadOnlyList<string> Messages(IEnumerable<InteractionValidationError> errors) =>
        errors.Select(Message).ToArray();
}
