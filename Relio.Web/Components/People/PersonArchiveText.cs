using Relio.Application.Time;
using Relio.Web.Time;

namespace Relio.Web.Components.People;

/// <summary>
/// The words for archiving, restoring and deleting a person (issue #26): the archived note on the
/// profile, the delete confirmation and the three snackbar messages. Relio's voice (sentence case,
/// no exclamation marks), kept out of the component so it is unit tested without rendering.
/// </summary>
/// <remarks>
/// Neither a snackbar message nor the page title ever carries the person's name: both can outlive
/// the screen (history, screen readers, tab lists), and a name is private. Only the confirmation
/// dialog names who is about to be deleted, because it is the one place that must be unmistakable.
/// </remarks>
public static class PersonArchiveText
{
    /// <summary>What the archived note says about the person's data, so archiving never reads as losing anything.</summary>
    public const string ArchivedNoteDetail = "Hidden from your lists and reminders. Everything you recorded is kept.";

    /// <summary>The body of the delete confirmation. States what goes and that it is final.</summary>
    public const string DeleteMessage =
        "This removes their profile, interactions, notes, reminders and difficult moments. It can't be undone.";

    /// <summary>The confirm button of the delete confirmation: the same verb as the menu item, with the weight spelled out.</summary>
    public const string DeleteConfirmLabel = "Delete permanently";

    /// <summary>The snackbar after archiving. No name.</summary>
    public const string Archived = "Person archived";

    /// <summary>The snackbar after restoring. No name.</summary>
    public const string Restored = "Person restored";

    /// <summary>The snackbar after deleting. No name.</summary>
    public const string Deleted = "Person deleted";

    /// <summary>The title of the delete confirmation: "Delete Ada Lovelace?".</summary>
    public static string DeleteTitle(string displayName) => $"Delete {displayName}?";

    /// <summary>
    /// "Archived on 3 March" (with the year when it is not this one): the day the person was archived
    /// in the viewer's calendar, not in UTC. "Archived" alone when the time is unknown
    /// (<paramref name="archivedAtUtc"/> is <see langword="null"/>).
    /// </summary>
    /// <param name="archivedAtUtc">
    /// <c>Person.ArchivedAtUtc</c>. SQL Server hands back <see cref="DateTimeKind.Unspecified"/>;
    /// it is always a UTC instant, so it is treated as one whatever its kind says.
    /// </param>
    /// <param name="timeZone">The signed-in user's time zone.</param>
    /// <param name="today">Today in that time zone, from <c>IUserTimeZoneService</c> (never a clock in a component).</param>
    public static string ArchivedOn(DateTime? archivedAtUtc, TimeZoneInfo timeZone, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (archivedAtUtc is not { } archivedAt)
        {
            return "Archived";
        }

        var instant = new DateTimeOffset(DateTime.SpecifyKind(archivedAt, DateTimeKind.Utc));
        return $"Archived on {DateDisplay.Format(UserCalendar.ToUserDate(instant, timeZone), today)}";
    }
}
