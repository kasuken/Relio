using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Reminders;
using Relio.Web.Components.Pages;
using Relio.Web.Tests.Settings;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests;

public class UnsubscribePageTests
{
    private static BunitContext CreateContext(FakeUnsubscribeService unsubscribeService)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IUnsubscribeService>(unsubscribeService);
        context.Render<MudPopoverProvider>();
        context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Valid_token_unsubscribes_and_shows_success_message()
    {
        var service = new FakeUnsubscribeService(result: true);
        await using var context = CreateContext(service);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/unsubscribe?token=valid-token-123");

        var cut = context.Render<Unsubscribe>();

        cut.WaitForAssertion(() =>
        {
            service.ReceivedTokens.Should().Contain("valid-token-123");
            cut.Find("[data-testid='unsubscribe-success']").Should().NotBeNull();
            cut.FindAll("[data-testid='unsubscribe-invalid']").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Invalid_token_shows_invalid_message()
    {
        var service = new FakeUnsubscribeService(result: false);
        await using var context = CreateContext(service);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/unsubscribe?token=bad-token");

        var cut = context.Render<Unsubscribe>();

        cut.WaitForAssertion(() =>
        {
            service.ReceivedTokens.Should().Contain("bad-token");
            cut.Find("[data-testid='unsubscribe-invalid']").Should().NotBeNull();
            cut.FindAll("[data-testid='unsubscribe-success']").Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_or_whitespace_token_shows_invalid_message_without_calling_service(string? token)
    {
        var service = new FakeUnsubscribeService(result: true);
        await using var context = CreateContext(service);
        var nav = context.Services.GetRequiredService<NavigationManager>();
        if (token is not null)
        {
            nav.NavigateTo($"/unsubscribe?token={token}");
        }
        else
        {
            nav.NavigateTo("/unsubscribe");
        }

        var cut = context.Render<Unsubscribe>();

        cut.WaitForAssertion(() =>
        {
            service.ReceivedTokens.Should().BeEmpty();
            cut.Find("[data-testid='unsubscribe-invalid']").Should().NotBeNull();
            cut.FindAll("[data-testid='unsubscribe-success']").Should().BeEmpty();
        });
    }
}
