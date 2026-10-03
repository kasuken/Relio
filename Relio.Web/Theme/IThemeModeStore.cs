namespace Relio.Web.Theme;

/// <summary>
/// Persists the user's <see cref="ThemeMode"/> preference and mirrors the resolved light/dark
/// choice onto the document, so CSS (see wwwroot/app.css) picks the right tokens.
/// </summary>
/// <remarks>
/// This is per-browser storage (see wwwroot/js/theme.js). Per-user, server-side persistence
/// comes with accounts (epic #14); once Identity lands, this should also read/write the
/// signed-in user's stored preference.
/// </remarks>
public interface IThemeModeStore
{
    /// <summary>Reads the stored preference, or <see cref="ThemeMode.System"/> if none is stored.</summary>
    ValueTask<ThemeMode> GetPreferenceAsync();

    /// <summary>Persists the chosen preference for this browser.</summary>
    ValueTask SetPreferenceAsync(ThemeMode mode);

    /// <summary>Sets the resolved (already system-aware) light/dark choice on the document.</summary>
    ValueTask ApplyResolvedThemeAsync(bool isDark);
}
