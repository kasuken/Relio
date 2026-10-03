using Microsoft.JSInterop;

namespace Relio.Web.Theme;

/// <summary>
/// <see cref="IThemeModeStore"/> backed by localStorage via wwwroot/js/theme.js
/// (loaded as a plain, self-hosted script in App.razor - no third-party requests).
/// </summary>
public sealed class JsThemeModeStore(IJSRuntime js) : IThemeModeStore
{
    public async ValueTask<ThemeMode> GetPreferenceAsync()
    {
        var value = await js.InvokeAsync<string>("Relio.theme.getPreference");
        return Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var mode) ? mode : ThemeMode.System;
    }

    public ValueTask SetPreferenceAsync(ThemeMode mode) =>
        js.InvokeVoidAsync("Relio.theme.setPreference", mode.ToString().ToLowerInvariant());

    public ValueTask ApplyResolvedThemeAsync(bool isDark) =>
        js.InvokeVoidAsync("Relio.theme.applyResolvedTheme", isDark);
}
