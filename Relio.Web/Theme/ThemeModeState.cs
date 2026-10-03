namespace Relio.Web.Theme;

/// <summary>
/// Circuit-scoped light/dark state shared by the app bar's theme menu, the Settings page and
/// <c>MainLayout</c>. Keeps the resolved <see cref="IsDark"/> value in sync with the user's
/// <see cref="Mode"/> choice and, when it is <see cref="ThemeMode.System"/>, with the browser's
/// reported system setting.
/// </summary>
/// <remarks>
/// Registered scoped (see Program.cs), so each Blazor Server circuit gets its own instance.
/// Persistence is per-browser today via <see cref="IThemeModeStore"/>; per-user, server-side
/// persistence comes with accounts (epic #14).
/// </remarks>
public sealed class ThemeModeState(IThemeModeStore store)
{
    private bool _systemIsDark;

    /// <summary>The user's chosen preference.</summary>
    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>The resolved light/dark value: <see cref="Mode"/> itself, or the system setting when <see cref="Mode"/> is <see cref="ThemeMode.System"/>.</summary>
    public bool IsDark { get; private set; }

    /// <summary>Raised whenever <see cref="Mode"/> or <see cref="IsDark"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Loads the stored preference and resolves it against the current system setting.</summary>
    public async Task InitializeAsync(bool systemIsDark)
    {
        Mode = await store.GetPreferenceAsync();
        _systemIsDark = systemIsDark;
        await RecomputeAsync();
    }

    /// <summary>Reports a change in the browser/OS light-dark setting. Only affects <see cref="IsDark"/> while <see cref="Mode"/> is <see cref="ThemeMode.System"/>.</summary>
    public async Task SetSystemPreferenceAsync(bool isDark)
    {
        _systemIsDark = isDark;
        if (Mode == ThemeMode.System)
        {
            await RecomputeAsync();
        }
    }

    /// <summary>Sets and persists the user's preference.</summary>
    public async Task SetModeAsync(ThemeMode mode)
    {
        Mode = mode;
        await store.SetPreferenceAsync(mode);
        await RecomputeAsync();
    }

    private async Task RecomputeAsync()
    {
        IsDark = Mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => _systemIsDark,
        };

        await store.ApplyResolvedThemeAsync(IsDark);
        Changed?.Invoke();
    }
}
