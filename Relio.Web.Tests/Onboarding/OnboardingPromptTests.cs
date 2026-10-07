using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Onboarding;
using Relio.Web.Components.Onboarding;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Onboarding;

public sealed class OnboardingPromptTests
{
    [Fact]
    public async Task Renders_only_for_a_pending_user_and_skips_persistently()
    {
        var onboarding = new FakeOnboardingService();
        await using var context = CreateContext(onboarding);

        var cut = context.Render<OnboardingPrompt>();

        cut.Find("[data-testid='onboarding-prompt-start']").GetAttribute("href").Should().Be("/onboarding");
        cut.Find("[data-testid='onboarding-prompt-skip']").Click();

        cut.WaitForAssertion(() =>
        {
            onboarding.Dismissals.Should().Be(1);
            cut.FindAll("[data-testid='onboarding-prompt']").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Dismissed_user_has_no_dashboard_prompt()
    {
        var onboarding = new FakeOnboardingService(isPending: false);
        await using var context = CreateContext(onboarding);

        var cut = context.Render<OnboardingPrompt>();

        cut.FindAll("[data-testid='onboarding-prompt']").Should().BeEmpty();
        cut.FindAll("[data-testid='onboarding-prompt-error']").Should().BeEmpty();
    }

    [Fact]
    public async Task Service_failure_is_explicit_and_can_be_retried()
    {
        var onboarding = new FakeOnboardingService
        {
            ThrowOnNextGet = new InvalidOperationException("failure"),
        };
        await using var context = CreateContext(onboarding);

        var cut = context.Render<OnboardingPrompt>();

        cut.Find("[data-testid='onboarding-prompt-error']").TextContent
            .Should().Contain("couldn't be checked");
        cut.Find("[data-testid='onboarding-prompt-retry']").Click();

        cut.WaitForAssertion(() =>
        {
            onboarding.StateReads.Should().Be(2);
            cut.Find("[data-testid='onboarding-prompt']").Should().NotBeNull();
        });
    }

    [Fact]
    public async Task Failed_skip_keeps_the_prompt_visible_and_can_be_retried()
    {
        var onboarding = new FakeOnboardingService
        {
            ThrowOnNextDismiss = new InvalidOperationException("failure"),
        };
        await using var context = CreateContext(onboarding);

        var cut = context.Render<OnboardingPrompt>();
        cut.Find("[data-testid='onboarding-prompt-skip']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='onboarding-prompt-dismiss-error']").Should().NotBeNull();
            cut.Find("[data-testid='onboarding-prompt']").Should().NotBeNull();
        });

        cut.Find("[data-testid='onboarding-prompt-skip']").Click();

        cut.WaitForAssertion(() =>
        {
            onboarding.Dismissals.Should().Be(1);
            cut.FindAll("[data-testid='onboarding-prompt']").Should().BeEmpty();
        });
    }

    private static BunitContext CreateContext(FakeOnboardingService onboarding)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddLogging();
        context.Services.AddSingleton<IOnboardingService>(onboarding);
        return context;
    }
}
