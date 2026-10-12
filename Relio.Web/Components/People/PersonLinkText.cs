using Relio.Domain;

namespace Relio.Web.Components.People;

/// <summary>
/// The words for connections between two people (issue #129): the profile section, the role names
/// (what the other person is to the one you are looking at), the dialog and the snackbars. Relio's
/// voice (sentence case, no exclamation marks), kept out of the components so it is unit tested
/// without rendering. No snackbar or title carries a person's name.
/// </summary>
public static class PersonLinkText
{
    /// <summary>The heading of the profile section.</summary>
    public const string SectionTitle = "Connections";

    /// <summary>The label of the add button on the profile.</summary>
    public const string AddLabel = "Add a connection";

    /// <summary>The title of the dialog when adding.</summary>
    public const string AddTitle = "Add a connection";

    /// <summary>The title of the dialog when editing.</summary>
    public const string EditTitle = "Edit connection";

    /// <summary>The snackbar after adding.</summary>
    public const string Added = "Connection added";

    /// <summary>The snackbar after changing the kind.</summary>
    public const string Updated = "Connection updated";

    /// <summary>The snackbar after removing.</summary>
    public const string Removed = "Connection removed";

    /// <summary>The title of the removal confirmation. No name.</summary>
    public const string RemoveTitle = "Remove this connection?";

    /// <summary>The body of the removal confirmation: what goes, and what stays.</summary>
    public const string RemoveMessage = "It is removed from both profiles. Both people stay in your list.";

    /// <summary>The confirm button of the removal confirmation.</summary>
    public const string RemoveConfirm = "Remove";

    /// <summary>Shown in the dialog when the list of people to choose from has nobody in it.</summary>
    public const string NoCandidates = "No other active people to connect.";

    /// <summary>Shown (snackbar) when a connection was removed elsewhere in the meantime.</summary>
    public const string Gone = "This connection is no longer available.";

    /// <summary>Shown in the dialog when the person chosen is no longer in the list.</summary>
    public const string PersonGone = "That person is no longer in your list.";

    /// <summary>Shown when the connections could not be loaded.</summary>
    public const string LoadFailed = "Connections couldn't be loaded. Try again.";

    /// <summary>Shown in the dialog when the people to choose from could not be loaded.</summary>
    public const string CandidatesLoadFailed = "The people in your list couldn't be loaded. Try again.";

    /// <summary>Shown when a save failed for a reason that is not a rule.</summary>
    public const string SaveFailed = "The connection couldn't be saved. Try again.";

    /// <summary>The note in the dialog about editing: the two people cannot change.</summary>
    public const string EditNote = "To connect a different person, remove this connection and add a new one.";

    /// <summary>The explanation in the dialog that the connection shows on both profiles.</summary>
    public const string BothProfilesNote = "It shows on both profiles.";

    /// <summary>What the other person is to the profile being viewed: "Parent", "Direct report".</summary>
    public static string Role(PersonLinkKind kind) => kind switch
    {
        PersonLinkKind.Partner => "Partner",
        PersonLinkKind.FormerPartner => "Former partner",
        PersonLinkKind.Parent => "Parent",
        PersonLinkKind.Child => "Child",
        PersonLinkKind.Sibling => "Sibling",
        PersonLinkKind.Grandparent => "Grandparent",
        PersonLinkKind.Grandchild => "Grandchild",
        PersonLinkKind.Relative => "Relative",
        PersonLinkKind.Friend => "Friend",
        PersonLinkKind.Colleague => "Colleague",
        PersonLinkKind.Manager => "Manager",
        PersonLinkKind.DirectReport => "Direct report",
        PersonLinkKind.Neighbour => "Neighbour",
        PersonLinkKind.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown link kind."),
    };

    /// <summary>The label of the kind select: "What are they to Anna?".</summary>
    public static string KindLabel(string firstName) => $"What are they to {firstName}?";

    /// <summary>The empty state on a profile: "No connections yet. ...".</summary>
    public static string Empty(string firstName) =>
        $"No connections yet. Link {firstName} to family, a partner or colleagues in your list.";

    /// <summary>The accessible name of the edit button of one connection.</summary>
    public static string EditLabel(string displayName) => $"Edit connection with {displayName}";

    /// <summary>The accessible name of the remove button of one connection.</summary>
    public static string RemoveLabel(string displayName) => $"Remove connection with {displayName}";
}
