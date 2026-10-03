using Relio.Web.Theme;

namespace Relio.Web.Tests.Theme;

/// <summary>An in-memory <see cref="IThemeModeStore"/> that records calls instead of touching JS interop.</summary>
internal sealed class FakeThemeModeStore : IThemeModeStore
{
    public ThemeMode StoredPreference { get; set; } = ThemeMode.System;

    public int ApplyCallCount { get; private set; }

    public bool? LastApplied { get; private set; }

    public ValueTask<ThemeMode> GetPreferenceAsync() => ValueTask.FromResult(StoredPreference);

    public ValueTask SetPreferenceAsync(ThemeMode mode)
    {
        StoredPreference = mode;
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyResolvedThemeAsync(bool isDark)
    {
        ApplyCallCount++;
        LastApplied = isDark;
        return ValueTask.CompletedTask;
    }
}
