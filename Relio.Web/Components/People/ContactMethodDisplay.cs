using MudBlazor;
using Relio.Domain;

namespace Relio.Web.Components.People;

/// <summary>
/// The words and icon for each <see cref="ContactMethodKind"/>, in one place so the form and the
/// profile never disagree. Pure; every method throws for a kind that is not defined, so a new
/// member cannot slip through unlabelled (a test covers every member).
/// </summary>
public static class ContactMethodDisplay
{
    /// <summary>The kind as a person reads it: "Email", "Phone", "Address", "Social", "Other".</summary>
    public static string KindName(ContactMethodKind kind) => kind switch
    {
        ContactMethodKind.Email => "Email",
        ContactMethodKind.Phone => "Phone",
        ContactMethodKind.Address => "Address",
        ContactMethodKind.Social => "Social",
        ContactMethodKind.Other => "Other",
        _ => throw Unknown(kind),
    };

    /// <summary>What the value input is called for this kind.</summary>
    public static string ValueLabel(ContactMethodKind kind) => kind switch
    {
        ContactMethodKind.Email => "Email address",
        ContactMethodKind.Phone => "Phone number",
        ContactMethodKind.Address => "Address",
        ContactMethodKind.Social => "Handle or link",
        ContactMethodKind.Other => "Details",
        _ => throw Unknown(kind),
    };

    /// <summary>The Outlined Material icon for this kind (decorative: always next to the words).</summary>
    public static string Icon(ContactMethodKind kind) => kind switch
    {
        ContactMethodKind.Email => Icons.Material.Outlined.Email,
        ContactMethodKind.Phone => Icons.Material.Outlined.Phone,
        ContactMethodKind.Address => Icons.Material.Outlined.Place,
        ContactMethodKind.Social => Icons.Material.Outlined.AlternateEmail,
        ContactMethodKind.Other => Icons.Material.Outlined.Notes,
        _ => throw Unknown(kind),
    };

    /// <summary>
    /// The heading a contact method gets on the profile: the kind, plus the label when there is one
    /// ("Email", "Phone · Mobile").
    /// </summary>
    public static string Heading(ContactMethodKind kind, string? label) =>
        string.IsNullOrWhiteSpace(label) ? KindName(kind) : $"{KindName(kind)} · {label.Trim()}";

    private static ArgumentOutOfRangeException Unknown(ContactMethodKind kind) =>
        new(nameof(kind), kind, "Unknown contact method kind.");
}
