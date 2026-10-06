using Microsoft.AspNetCore.Identity;

namespace Relio.Data.Identity;

/// <summary>
/// Relio's ASP.NET Core Identity user (epic #14). Lives in <c>Relio.Data</c>, not
/// <c>Relio.Domain</c>: it is Identity infrastructure (sign-in, password hashes, security
/// stamps), not a product entity, and <c>Relio.Domain</c> stays free of any framework dependency
/// (see the "User-scoped data pattern" section of AGENTS.md). Every other user-owned entity
/// (<c>Person</c>, <c>Tag</c>, <c>UserProfile</c>) references this user only indirectly, through
/// its <c>Id</c> stored as <c>IOwnedEntity.OwnerId</c> - they never take a navigation property to
/// it, so Domain/Application never need to know Identity exists.
/// </summary>
/// <remarks>
/// Email + password only (#15), plus <see cref="PendingEmail"/> for account settings (#18) and
/// <see cref="IsDisabled"/> for self-hosted administration (#19).
/// Two-factor fields already exist on <see cref="IdentityUser"/> itself, so #20 needs no model
/// change here. Product-facing profile data (display name, time zone) lives on
/// <c>Relio.Domain.UserProfile</c> instead, not here.
/// </remarks>
public sealed class RelioUser : IdentityUser
{
    /// <summary>
    /// The new email address awaiting confirmation after the user asked to change it in account
    /// settings (#18), or <see langword="null"/> when no change is pending. Stored server-side so
    /// the confirmation link only ever carries the user id and an opaque token - never an email
    /// address (see the gdpr-compliant skill: no personal data in URLs). Cleared once the change
    /// is confirmed. Overwritten by a newer request, which also invalidates the older link:
    /// change-email tokens are bound to the address they were generated for.
    /// </summary>
    [PersonalData]
    public string? PendingEmail { get; set; }

    /// <summary>
    /// Whether an Administrator disabled this account (self-hosted administration, #19). A disabled
    /// account can not sign in and its open sessions end at their next validation
    /// (<c>Account:Session:ValidationInterval</c>); everything it owns is left untouched, and
    /// enabling it again restores access exactly as it was.
    /// </summary>
    /// <remarks>
    /// A dedicated flag, deliberately not Identity's lockout: lockout is also cleared by a
    /// successful password reset (<c>ResetPassword.razor</c> sets the lockout end date to
    /// <see langword="null"/> so a user who regained access by email is not still locked out), so a
    /// lockout-based disable could be undone by the very person it was applied to.
    /// </remarks>
    public bool IsDisabled { get; set; }
}
