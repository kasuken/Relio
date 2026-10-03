namespace Relio.Web.Theme;

/// <summary>
/// The three kinds of entry on a person's timeline (see "The thread" in
/// docs/design-system/README.md). Each has its own marker shape, colour and icon so the type
/// is readable at a glance, and without colour.
/// </summary>
public enum EntryKind
{
    Interaction,
    Note,
    DifficultMoment,
}
