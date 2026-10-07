using Relio.Domain;

namespace Relio.Web.Components.People;

/// <summary>Plain-language labels for interaction categories.</summary>
public static class InteractionKindText
{
    /// <summary>The available interaction categories in stable picker order.</summary>
    public static IReadOnlyList<InteractionKind> All { get; } = Enum.GetValues<InteractionKind>();

    /// <summary>Returns the label shown in pickers and timelines.</summary>
    public static string Name(InteractionKind kind) => kind switch
    {
        InteractionKind.Call => "Call",
        InteractionKind.Meeting => "Meeting",
        InteractionKind.Message => "Message",
        InteractionKind.Event => "Event",
        InteractionKind.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown interaction type."),
    };
}
