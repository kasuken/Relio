namespace Relio.Application.Administration;

/// <summary>
/// Creates accounts, applying the instance's sign-up rules (<see cref="RegistrationOptions"/>,
/// issue #19). Unlike nearly every other Application service this one is anonymous - nobody is
/// signed in while registering - so it never touches <c>ICurrentUser</c> and never reads or writes
/// any user-owned data other than creating the new account's own profile row.
/// </summary>
/// <remarks>
/// The first account on an instance becomes its Administrator; see the "Self-hosted
/// administration" bullet of AGENTS.md for the rule and the race it guards against.
/// </remarks>
public interface IAccountRegistrationService
{
    /// <summary>
    /// Whether the visitor may see a sign-up form right now, and why not. Advisory: it lets pages
    /// render a calm explanation instead of a form, but <see cref="RegisterAsync"/> re-checks
    /// everything, so a crafted POST can never bypass it.
    /// </summary>
    /// <param name="invitationToken">The <c>invite</c> value from the link, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<RegistrationEligibility> GetEligibilityAsync(
        string? invitationToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an account if the instance's sign-up rules allow it. Password-policy and duplicate
    /// email failures are reported as <see cref="RegisterAccountStatus.IdentityErrors"/> and
    /// consume nothing (an invitation stays usable).
    /// </summary>
    /// <param name="request">The submitted sign-up form.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<RegisterAccountResult> RegisterAsync(
        RegisterAccountRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Whether, and under which rule, a visitor may register.</summary>
public enum RegistrationAccess
{
    /// <summary>Registration is allowed under the instance's normal rules (open, or a valid invitation).</summary>
    Allowed,

    /// <summary>
    /// The instance has no accounts yet, so registration is allowed in every mode and the new
    /// account will be the instance's Administrator.
    /// </summary>
    FirstAccount,

    /// <summary>Invitation-only mode and no invitation was supplied.</summary>
    RequiresInvitation,

    /// <summary>Invitation-only mode and the supplied invitation is unknown, expired or already used.</summary>
    InvitationInvalid,

    /// <summary>Registration is closed.</summary>
    Closed,
}

/// <summary>The answer to <see cref="IAccountRegistrationService.GetEligibilityAsync"/>.</summary>
/// <param name="Access">Whether registration is allowed, and why not.</param>
/// <param name="InvitedEmail">
/// For a valid invitation, the address it was created for (the form pre-fills it and the
/// registration must use it); otherwise <see langword="null"/>.
/// </param>
public sealed record RegistrationEligibility(RegistrationAccess Access, string? InvitedEmail = null)
{
    /// <summary>Whether a sign-up form should be shown.</summary>
    public bool CanRegister => Access is RegistrationAccess.Allowed or RegistrationAccess.FirstAccount;
}

/// <summary>Input to <see cref="IAccountRegistrationService.RegisterAsync"/>.</summary>
/// <param name="Email">The new account's email address (also its user name).</param>
/// <param name="Password">The chosen password; validated against the Identity password policy.</param>
/// <param name="TimeZoneId">
/// The browser's IANA time zone id, stored on the new account's profile. Anything that is not a
/// valid id falls back to UTC (registration should never fail on a hidden form field).
/// </param>
/// <param name="InvitationToken">The invitation token from the link, or <see langword="null"/>.</param>
public sealed record RegisterAccountRequest(
    string Email, string Password, string? TimeZoneId, string? InvitationToken);

/// <summary>How a registration attempt ended.</summary>
public enum RegisterAccountStatus
{
    /// <summary>The account was created.</summary>
    Succeeded,

    /// <summary>Identity rejected the account (weak password, duplicate email, ...); see <see cref="RegisterAccountResult.Errors"/>.</summary>
    IdentityErrors,

    /// <summary>Registration is closed.</summary>
    Closed,

    /// <summary>Invitation-only mode and no invitation was supplied.</summary>
    InvitationRequired,

    /// <summary>Invitation-only mode and the invitation is unknown, expired or already used.</summary>
    InvitationInvalid,

    /// <summary>The invitation is valid but was created for a different email address. It stays usable.</summary>
    InvitationEmailMismatch,
}

/// <summary>One Identity error (e.g. <c>PasswordTooShort</c>) and its description.</summary>
public sealed record RegistrationError(string Code, string Description);

/// <summary>The outcome of <see cref="IAccountRegistrationService.RegisterAsync"/>.</summary>
/// <param name="Status">How the attempt ended.</param>
/// <param name="UserId">The new account's id when <paramref name="Status"/> is <see cref="RegisterAccountStatus.Succeeded"/>.</param>
/// <param name="IsAdministrator">Whether the new account became the instance's Administrator.</param>
/// <param name="Errors">Identity's errors when <paramref name="Status"/> is <see cref="RegisterAccountStatus.IdentityErrors"/>.</param>
public sealed record RegisterAccountResult(
    RegisterAccountStatus Status,
    string? UserId,
    bool IsAdministrator,
    IReadOnlyList<RegistrationError> Errors)
{
    /// <summary>A result with no errors and no new account (a refusal).</summary>
    public static RegisterAccountResult Refused(RegisterAccountStatus status) => new(status, null, false, []);

    /// <summary>A successful registration.</summary>
    public static RegisterAccountResult Success(string userId, bool isAdministrator) =>
        new(RegisterAccountStatus.Succeeded, userId, isAdministrator, []);

    /// <summary>Identity rejected the account.</summary>
    public static RegisterAccountResult Failed(IReadOnlyList<RegistrationError> errors) =>
        new(RegisterAccountStatus.IdentityErrors, null, false, errors);

    /// <summary>Whether the account was created.</summary>
    public bool Succeeded => Status == RegisterAccountStatus.Succeeded;
}
