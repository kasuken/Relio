using MudBlazor;

namespace Relio.Web.Components.Shared;

/// <summary>
/// Helpers for showing Relio's shared dialogs without every caller unpacking a
/// <see cref="DialogResult"/> by hand.
/// </summary>
public static class DialogServiceExtensions
{
    /// <summary>
    /// Shows a <see cref="ConfirmDialog"/> and awaits the user's choice.
    /// </summary>
    /// <returns><see langword="true"/> when the user confirmed; <see langword="false"/> when cancelled or dismissed.</returns>
    public static async Task<bool> ShowConfirmAsync(
        this IDialogService dialogService,
        string title,
        string message,
        string confirmLabel = "Confirm",
        string cancelLabel = "Cancel",
        bool destructive = false)
    {
        ArgumentNullException.ThrowIfNull(dialogService);

        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Title, title },
            { x => x.Message, message },
            { x => x.ConfirmLabel, confirmLabel },
            { x => x.CancelLabel, cancelLabel },
            { x => x.Destructive, destructive },
        };

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall };

        var dialog = await dialogService.ShowAsync<ConfirmDialog>(title, parameters, options);
        var result = await dialog.Result;

        return result is { Canceled: false };
    }
}
