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
/// Intentionally minimal for #15: email + password only. Two-factor fields already exist on
/// <see cref="IdentityUser"/> itself, so #20 needs no model change here.
/// </remarks>
public sealed class RelioUser : IdentityUser;
