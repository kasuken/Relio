using System.Globalization;
using Relio.Application.People;

namespace Relio.Web.Components.Settings;

/// <summary>Which kind of label a settings component manages.</summary>
public enum LabelKind
{
    /// <summary>A relationship type such as "Friend".</summary>
    RelationshipType,

    /// <summary>A tag such as "Climbing".</summary>
    Tag,
}

/// <summary>
/// The words of the relationship type and tag settings (issue #25), as pure functions so they are
/// tested without rendering. Plain, calm sentences in the user's own terms; always the invariant
/// culture, like the rest of Relio's copy.
/// </summary>
public static class LabelSettingsText
{
    /// <summary>The meta line under a label: how many people have it.</summary>
    public static string UsedBy(int people) => people switch
    {
        0 => "No one yet",
        1 => "1 person",
        _ => string.Create(CultureInfo.InvariantCulture, $"{people:N0} people"),
    };

    /// <summary>The start of the dialog that asks what happens to people when their type is removed.</summary>
    public static string TypeInUse(int people) => people switch
    {
        1 => "1 person has this relationship type.",
        _ => string.Create(CultureInfo.InvariantCulture, $"{people:N0} people have this relationship type."),
    };

    /// <summary>The message of the confirmation before a tag is removed.</summary>
    public static string TagRemoval(int people) => people switch
    {
        0 => "No one has this tag.",
        1 => "1 person has this tag. Removing it takes it off them; nothing else about them changes.",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"{people:N0} people have this tag. Removing it takes it off them; nothing else about them changes."),
    };

    /// <summary>The message shown next to the name field for a refused name.</summary>
    public static string Message(LabelValidationError error, LabelKind kind) => error switch
    {
        LabelValidationError.NameRequired => "Enter a name.",
        LabelValidationError.NameTooLong => "Keep the name to 50 characters or fewer.",
        LabelValidationError.NameTaken => kind switch
        {
            LabelKind.RelationshipType => "You already have a relationship type with that name.",
            LabelKind.Tag => "You already have a tag with that name.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown label kind."),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown label validation error."),
    };
}
