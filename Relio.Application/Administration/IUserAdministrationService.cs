namespace Relio.Application.Administration;

/// <summary>
/// What an instance's Administrator can do (issue #19): see who has an account, disable and
/// re-enable accounts, and invite people when sign-up is invitation-only. Every method requires
/// the current user to be an active Administrator (checked against the database each time) and
/// throws <see cref="AdministratorRequiredException"/> otherwise.
/// </summary>
/// <remarks>
/// This is a deliberate, narrow surface: it exposes account facts and invitations only - never
/// people, notes, interactions, difficult moments, tags or even the display name. An Administrator
/// must be able to run an instance without being able to read anyone's private notebook, and a
/// test proves the types in this interface can not carry owned data. Never add a role-based bypass
/// to an owned-data service (see the "Self-hosted administration" bullet of AGENTS.md).
/// </remarks>
public interface IUserAdministrationService
{
    /// <summary>Every account on the instance, ordered by email.</summary>
    Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops an account from signing in and ends its open sessions (within
    /// <c>Account:Session:ValidationInterval</c>). Nothing the account owns is changed. An
    /// Administrator can not disable their own account, which together with "a disabled
    /// Administrator can not administer" means an active Administrator always remains.
    /// </summary>
    /// <param name="userId">The account to disable.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AccountChangeResult> DisableAccountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Lets a disabled account sign in again.</summary>
    /// <param name="userId">The account to enable.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AccountChangeResult> EnableAccountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Invitations that have not been used and have not expired, soonest-expiring first.</summary>
    Task<IReadOnlyList<InvitationSummary>> ListPendingInvitationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a single-use invitation for an email address. A newer invitation for the same
    /// address replaces the older one. Only meaningful when <c>Registration:Mode</c> is
    /// <see cref="RegistrationMode.InviteOnly"/>. The plaintext token is returned once, in
    /// <see cref="CreatedInvitation.Token"/>; only its hash is stored.
    /// </summary>
    /// <param name="email">The address the invitation is for.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<CreateInvitationResult> CreateInvitationAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Withdraws a pending invitation. Returns <see langword="false"/> when there is none with that id.</summary>
    /// <param name="invitationId">The invitation to withdraw.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default);
}

/// <summary>An account as an Administrator sees it: who it is and whether it can sign in. Nothing else.</summary>
/// <param name="UserId">The account's id.</param>
/// <param name="Email">The account's email address.</param>
/// <param name="EmailConfirmed">Whether the email address was confirmed.</param>
/// <param name="IsAdministrator">Whether the account is an Administrator.</param>
/// <param name="IsDisabled">Whether an Administrator disabled the account.</param>
/// <param name="IsLockedOut">Whether the account is temporarily locked out after failed sign-ins.</param>
/// <param name="IsCurrentUser">Whether this is the Administrator asking.</param>
public sealed record AccountSummary(
    string UserId,
    string Email,
    bool EmailConfirmed,
    bool IsAdministrator,
    bool IsDisabled,
    bool IsLockedOut,
    bool IsCurrentUser);

/// <summary>The outcome of disabling or enabling an account.</summary>
public enum AccountChangeResult
{
    /// <summary>The account is now in the requested state (including when it already was).</summary>
    Succeeded,

    /// <summary>No account has that id.</summary>
    NotFound,

    /// <summary>An Administrator can not disable their own account.</summary>
    CannotChangeOwnAccount,

    /// <summary>Disabling the account would leave no active Administrator.</summary>
    LastActiveAdministrator,
}

/// <summary>A pending invitation. Never includes the token (only its hash is stored).</summary>
/// <param name="Id">The invitation's id.</param>
/// <param name="Email">The address it was created for.</param>
/// <param name="CreatedAtUtc">When it was created.</param>
/// <param name="ExpiresAtUtc">When it stops working.</param>
public sealed record InvitationSummary(Guid Id, string Email, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);

/// <summary>A just-created invitation, including the one-time plaintext <paramref name="Token"/>.</summary>
/// <param name="Id">The invitation's id.</param>
/// <param name="Email">The address it was created for.</param>
/// <param name="Token">The secret token for the link. Shown once; only its hash is stored.</param>
/// <param name="ExpiresAtUtc">When it stops working.</param>
public sealed record CreatedInvitation(Guid Id, string Email, string Token, DateTime ExpiresAtUtc);

/// <summary>Why an invitation was or was not created.</summary>
public enum CreateInvitationStatus
{
    /// <summary>The invitation was created.</summary>
    Created,

    /// <summary>The address is not a valid email address.</summary>
    InvalidEmail,

    /// <summary>An account with that address already exists.</summary>
    AlreadyHasAccount,

    /// <summary>The instance is not in <see cref="RegistrationMode.InviteOnly"/>, so invitations do nothing.</summary>
    InvitationsNotInUse,
}

/// <summary>The outcome of <see cref="IUserAdministrationService.CreateInvitationAsync"/>.</summary>
/// <param name="Status">Whether the invitation was created, and why not.</param>
/// <param name="Invitation">The invitation when <paramref name="Status"/> is <see cref="CreateInvitationStatus.Created"/>.</param>
public sealed record CreateInvitationResult(CreateInvitationStatus Status, CreatedInvitation? Invitation);
