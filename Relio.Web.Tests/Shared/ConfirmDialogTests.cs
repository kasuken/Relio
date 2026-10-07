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

    [Theory]
    [InlineData(true, "mud-button-filled-error")]
    [InlineData(false, "mud-button-filled-primary")]
    public async Task A_destructive_confirmation_uses_the_error_colour_and_a_plain_one_uses_primary(bool destructive, string expectedClass)
    {
        await using var context = new BunitContext();
        context.UseMudBlazor();
        context.Render<MudPopoverProvider>();
        var dialogHost = context.Render<MudDialogProvider>();
        var dialogService = context.Services.GetRequiredService<IDialogService>();

        var resultTask = dialogService.ShowConfirmAsync("Delete Marta?", "It can't be undone.", "Delete permanently", destructive: destructive);

        var confirm = dialogHost.Find("[data-testid='confirm-dialog-confirm']");
        confirm.ClassList.Should().Contain(expectedClass);
        confirm.Click();
        (await resultTask).Should().BeTrue();
    }

    [Fact]
    public async Task The_buttons_carry_stable_test_ids()
    {
        await using var context = new BunitContext();
        context.UseMudBlazor();
        context.Render<MudPopoverProvider>();
        var dialogHost = context.Render<MudDialogProvider>();
        var dialogService = context.Services.GetRequiredService<IDialogService>();

        var resultTask = dialogService.ShowConfirmAsync("Archive Marta?", "You can restore her later.", confirmLabel: "Archive", cancelLabel: "Not now");

        dialogHost.Find("[data-testid='confirm-dialog-confirm']").TextContent.Trim().Should().Be("Archive");
        dialogHost.Find("[data-testid='confirm-dialog-cancel']").TextContent.Trim().Should().Be("Not now");
        dialogHost.Find("[data-testid='confirm-dialog-cancel']").Click();
        (await resultTask).Should().BeFalse();
    }
}
