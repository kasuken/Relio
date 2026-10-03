using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Web.Components.Shared;

namespace Relio.Web.Tests.Shared;

public class ConfirmDialogTests
{
    // BunitContext is created and disposed per test via "await using" (rather than the test
    // class inheriting it) because MudBlazor registers at least one IAsyncDisposable-only
    // service (PointerEventsNoneService, used to lock body scroll under an open dialog); xUnit
    // disposes a test class synchronously, which throws for that service. Disposing the
    // context asynchronously here avoids that entirely.
    [Fact]
    public async Task ShowConfirmAsync_returns_true_when_the_confirm_button_is_clicked()
    {
        await using var context = new BunitContext();
        context.UseMudBlazor();

        context.Render<MudPopoverProvider>();
        var dialogHost = context.Render<MudDialogProvider>();
        var dialogService = context.Services.GetRequiredService<IDialogService>();

        var resultTask = dialogService.ShowConfirmAsync("Archive Marta?", "You can restore her later.", confirmLabel: "Archive");

        dialogHost.FindAll("button").Single(b => b.TextContent.Contains("Archive")).Click();

        var result = await resultTask;

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShowConfirmAsync_returns_false_when_cancelled()
    {
        await using var context = new BunitContext();
        context.UseMudBlazor();

        context.Render<MudPopoverProvider>();
        var dialogHost = context.Render<MudDialogProvider>();
        var dialogService = context.Services.GetRequiredService<IDialogService>();

        var resultTask = dialogService.ShowConfirmAsync("Archive Marta?", "You can restore her later.", confirmLabel: "Archive");

        dialogHost.FindAll("button").Single(b => b.TextContent.Contains("Cancel")).Click();

        var result = await resultTask;

        result.Should().BeFalse();
    }
}
