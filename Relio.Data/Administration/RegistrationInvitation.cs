namespace Relio.Data.Administration;

/// <summary>
/// A pending invitation to create an account on an invitation-only instance (issue #19). Lives in
/// <c>Relio.Data</c> next to <c>RelioUser</c>: it is instance administration, not user content, so
/// it is deliberately not an <c>IOwnedEntity</c> and nothing in <c>Relio.Domain</c> knows about it.
/// </summary>
/// <remarks>
/// A row exists only while the invitation is pending: a successful registration deletes it, an
/// Administrator can revoke it, and expired rows are purged. Only the SHA-256 hash of the 256-bit
/// random token is stored (see <see cref="InvitationTokens"/>), so a copy of the database does not
/// let anyone sign up.
/// </remarks>
public sealed class RegistrationInvitation
{
    /// <summary>The invitation's id, used to revoke it. Not secret and not part of the link.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The address the invitation was created for, as the Administrator typed it.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The Identity-normalized form of <see cref="Email"/>, which registration is compared against.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>Upper-case hex SHA-256 of the token in the invitation link.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The Administrator who created it. A plain id, not a foreign key, so it never blocks deleting an account.</summary>
    public string CreatedByUserId { get; set; } = string.Empty;

    /// <summary>When the invitation was created (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the invitation stops working (UTC).</summary>
    public DateTime ExpiresAtUtc { get; set; }
}
