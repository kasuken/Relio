namespace Relio.Web.Theme;

/// <summary>
/// A user's light/dark preference. <see cref="System"/> is the default: Relio follows the
/// browser/OS setting until the user overrides it from the app bar or Settings.
/// </summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}
