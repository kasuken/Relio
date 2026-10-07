using Relio.Application.People;

namespace Relio.Web.Components.People;

/// <summary>
/// The words of the merge page (issue #28), in Relio's voice: sentence case, calm, no exclamation
/// marks, "you" never "I". Kept out of the component so they are unit tested without rendering.
/// </summary>
/// <remarks>
/// A name appears only in the confirmation dialog (the one place that must be unmistakable about who
/// goes) and on the page itself, never in the snackbar, the page title or the address: all three can
/// outlive the screen.
/// </remarks>
public static class PersonMergeText
{
    /// <summary>The page's heading and the confirm button's label share the verb; this is the heading.</summary>
    public const string Heading = "Merge profiles";

    /// <summary>The side of a choice that is the profile being kept.</summary>
    public const string ThisProfile = "This profile";

    /// <summary>The side of a choice that is merged in and removed.</summary>
    public const string OtherProfile = "Other profile";

    /// <summary>The choice that keeps both texts.</summary>
    public const string KeepBoth = "Keep both";

    /// <summary>The wording of an active profile.</summary>
    public const string Active = "Active";

    /// <summary>The wording of an archived profile.</summary>
    public const string Archived = "Archived";

    /// <summary>The confirm button of the dialog and of the page: the same verb in both.</summary>
    public const string ConfirmLabel = "Merge profiles";

    /// <summary>The snackbar after a merge. No name.</summary>
    public const string Merged = "Profiles merged";

    /// <summary>Shown when the merge finds one of the profiles gone (removed elsewhere, or never the user's).</summary>
    public const string Gone = "One of these profiles is no longer in your list.";

    /// <summary>Shown above the preview when the two profiles do not disagree on anything.</summary>
    public const string NoConflicts = "These profiles don't disagree on anything. Their details will be combined.";

    /// <summary>The line under the choices: where everything recorded goes.</summary>
    public const string EverythingMoves = "Everything recorded about the other profile moves to the one you keep.";

    /// <summary>The label of a <see cref="MergeField"/> as a person reads it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="field"/> is not a defined value.</exception>
    public static string FieldLabel(MergeField field) => field switch
    {
        MergeField.Name => "Name",
        MergeField.Nickname => "Nickname",
        MergeField.RelationshipType => "Relationship",
        MergeField.Birthday => "Birthday",
        MergeField.HowWeMet => "How you met",
        MergeField.Details => "Details",
        MergeField.ArchivedState => "Status",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a known MergeField."),
    };

    /// <summary>The title of the confirmation: "Merge Jon Smith into John Smith?".</summary>
    public static string ConfirmTitle(string duplicateName, string primaryName) =>
        $"Merge {duplicateName} into {primaryName}?";

    /// <summary>The body of the confirmation. States what goes, where it moves and that it is final.</summary>
    public static string ConfirmMessage(string duplicateName, string primaryName) =>
        $"{duplicateName}'s profile will be removed. Everything recorded about them moves to {primaryName}. This can't be undone.";

    /// <summary>The note after the preview's contact methods when <paramref name="count"/> repeated details were combined.</summary>
    public static string RepeatedCombined(int count) => count == 1
        ? "1 repeated detail combined"
        : $"{count} repeated details combined";

    /// <summary>
    /// What a refused merge says: the limits get their own wording (the person form's wording is about
    /// adding one more), every other code uses the person form's message.
    /// </summary>
    public static string Problem(PersonValidationError error) => error switch
    {
        PersonValidationError.TooManyContactMethods =>
            "Together they have more than 20 contact methods. Remove some from one profile, then merge.",
        PersonValidationError.TooManyTags =>
            "Together they have more than 20 tags. Remove some from one profile, then merge.",
        PersonValidationError.HowWeMetTooLong or PersonValidationError.DetailsTooLong =>
            "Both texts together are too long. Keep one of them instead.",
        _ => PersonFormMessages.For(error).Message,
    };
}
