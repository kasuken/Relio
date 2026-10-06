namespace Relio.Application.Administration;

/// <summary>
/// Who may create an account on this instance (<c>Registration:Mode</c>, issue #19). While an
/// instance has no accounts at all, the first one can always be created in every mode - see
/// <see cref="RegistrationAccess.FirstAccount"/>.
/// </summary>
public enum RegistrationMode
{
    /// <summary>Anyone can sign up. The default, and what Relio did before issue #19.</summary>
    Open,

    /// <summary>Only someone holding a valid, unused invitation link can sign up.</summary>
    InviteOnly,

    /// <summary>Nobody can sign up; the operator has to change the setting first.</summary>
    Closed,
}
