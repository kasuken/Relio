using MudBlazor;

namespace Relio.Web.Theme;

/// <summary>
/// The icon, label and the "thread" marker/colour for one <see cref="EntryKind"/>.
/// </summary>
/// <param name="DisplayName">Sentence-case label, e.g. "Difficult moment".</param>
/// <param name="Icon">A MudBlazor Material Outlined icon path (see docs/design-system/README.md, Iconography).</param>
/// <param name="LabelCssClass">Colours text/icons in this kind's colour (defined in wwwroot/app.css).</param>
/// <param name="MarkerCssClass">Draws this kind's thread marker shape (defined in wwwroot/app.css).</param>
public sealed record EntryKindVisual(string DisplayName, string Icon, string LabelCssClass, string MarkerCssClass);

/// <summary>
/// Single source of truth mapping each <see cref="EntryKind"/> to its design-system icon and
/// colour, so every page shows interactions, notes and difficult moments the same, distinct way.
/// </summary>
public static class EntryKindVisuals
{
    public static IReadOnlyList<EntryKind> All { get; } = Enum.GetValues<EntryKind>();

    public static EntryKindVisual For(EntryKind kind) => kind switch
    {
        EntryKind.Interaction => new EntryKindVisual("Interaction", Icons.Material.Outlined.Forum, "rl-kind-interaction", "rl-marker-interaction"),
        EntryKind.Note => new EntryKindVisual("Note", Icons.Material.Outlined.StickyNote2, "rl-kind-note", "rl-marker-note"),
        EntryKind.DifficultMoment => new EntryKindVisual("Difficult moment", Icons.Material.Outlined.Thunderstorm, "rl-kind-moment", "rl-marker-moment"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entry kind."),
    };
}
