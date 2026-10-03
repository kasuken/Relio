namespace Relio.Web.Identity;

/// <summary>
/// Login/lockout/session configuration for issue #16, bound from the <c>Account</c>
/// configuration section. See <see cref="ServiceCollectionExtensions.AddRelioIdentity"/> for how
/// these values feed <c>IdentityOptions.Lockout</c> and the application cookie.
/// </summary>
public sealed class AccountOptions
{
    /// <summary>The configuration section name (<c>Account</c>).</summary>
    public const string SectionName = "Account";

    /// <summary>Account lockout settings. See <see cref="AccountLockoutOptions"/>.</summary>
    public AccountLockoutOptions Lockout { get; set; } = new();

    /// <summary>Sign-in cookie settings. See <see cref="AccountCookieOptions"/>.</summary>
    public AccountCookieOptions Cookie { get; set; } = new();

    /// <summary>Password reset token settings (issue #17). See <see cref="AccountPasswordResetOptions"/>.</summary>
    public AccountPasswordResetOptions PasswordReset { get; set; } = new();
}

/// <summary>
/// Account lockout settings, applied to <c>IdentityOptions.Lockout</c> in
/// <see cref="ServiceCollectionExtensions.AddRelioIdentity"/>. Defaults match the issue #16
/// acceptance criteria (5 failed attempts, 15 minutes).
/// </summary>
public sealed class AccountLockoutOptions
{
    /// <summary>
    /// How many failed sign-in attempts in a row lock the account out. Defaults to 5.
    /// </summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>
    /// How long an account stays locked out once <see cref="MaxFailedAccessAttempts"/> is reached.
    /// Defaults to 15 minutes.
    /// </summary>
    public TimeSpan DefaultLockoutTimeSpan { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether lockout is enabled for newly created accounts (not just ones that opt in after the
    /// fact). Defaults to <see langword="true"/> - every Relio account is lockout-protected from
    /// the moment it is created.
    /// </summary>
    public bool AllowedForNewUsers { get; set; } = true;
}

/// <summary>
/// Sign-in cookie settings, applied to the application cookie in
/// <see cref="ServiceCollectionExtensions.AddRelioIdentity"/>.
/// </summary>
public sealed class AccountCookieOptions
{
    /// <summary>
    /// The sliding-expiration lifetime for the sign-in cookie's ticket. Applies whether or not
    /// "Remember me" was checked - what "Remember me" (<c>isPersistent</c>, set per sign-in in
    /// <c>Login.razor</c>) actually changes is whether the cookie sent to the browser carries an
    /// <c>Expires</c>/<c>Max-Age</c> attribute at all: persistent (survives closing the browser)
    /// when checked, a plain session cookie (deleted when the browser closes) when not. Defaults
    /// to 14 days, a common "remember me" lifetime that still forces a fresh sign-in periodically.
    /// </summary>
    public TimeSpan ExpireTimeSpan { get; set; } = TimeSpan.FromDays(14);
}

/// <summary>
/// Password reset token settings (issue #17), applied to
/// <see cref="Relio.Web.Identity.PasswordResetTokenProviderOptions"/> - a dedicated token
/// provider/options type, not the same one email confirmation uses, so this lifespan can change
/// without affecting email confirmation's. See
/// <see cref="ServiceCollectionExtensions.AddRelioIdentity"/> and
/// <see cref="Relio.Web.Identity.PasswordResetTokenProvider{TUser}"/>'s remarks.
/// </summary>
public sealed class AccountPasswordResetOptions
{
    /// <summary>
    /// How long an emailed password reset link stays valid after it is generated. Defaults to 1
    /// hour - long enough to find the email, short enough that a stale, forwarded or leaked link
    /// does not stay usable indefinitely. The link is single-use regardless (see
    /// <c>Relio.Web.Components.Account.Pages.ResetPassword</c>'s remarks): resetting the password
    /// rotates the account's security stamp, which Identity's token verification is bound to.
    /// </summary>
    public TimeSpan TokenLifespan { get; set; } = TimeSpan.FromHours(1);
}
