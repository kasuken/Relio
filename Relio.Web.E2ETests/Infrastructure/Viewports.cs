using Microsoft.Playwright;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// Named viewport sizes for E2E tests (see docs/design-system/README.md for Relio's supported
/// breakpoints). Use these with <see cref="RelioAppFixture.NewPageAsync"/> rather than hand-rolled
/// pixel sizes, so every test asserts against the same phone/desktop boundary MudBlazor's
/// <c>Breakpoint.Md</c> drawer behaviour switches on.
/// </summary>
public static class Viewports
{
    /// <summary>A typical modern phone in portrait orientation (390x844, e.g. iPhone 12/13).</summary>
    public static ViewportSize Phone { get; } = new() { Width = 390, Height = 844 };

    /// <summary>A desktop browser window, wide enough to keep the nav drawer permanently open.</summary>
    public static ViewportSize Desktop { get; } = new() { Width = 1280, Height = 800 };
}
