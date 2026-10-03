using MudBlazor;

namespace Relio.Web.Components.Shared;

/// <summary>
/// Small, consistent vocabulary for the handful of snackbar messages Relio shows (saved,
/// restored, something went wrong). Default position/duration/behaviour is configured once, in
/// Program.cs, via <c>MudServicesConfiguration.SnackbarConfiguration</c>.
/// </summary>
/// <remarks>Relio is calm, never urgent (see docs/design-system/README.md): no red badges or counters, so <see cref="Problem"/> still reads as plain, factual text.</remarks>
public static class SnackbarExtensions
{
    /// <summary>A confirmation, e.g. "Note saved".</summary>
    public static void Saved(this ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Success);

    /// <summary>A neutral confirmation that doesn't warrant "success" styling, e.g. "Person restored".</summary>
    public static void Info(this ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Info);

    /// <summary>Something failed; says what happened, not just that it did.</summary>
    public static void Problem(this ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Error);
}
