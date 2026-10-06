using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Application.Administration;

namespace Relio.Data.Configurations;

/// <summary>
/// Seeds the one Identity role Relio uses, <see cref="RelioRoles.Administrator"/> (issue #19), as
/// model data (<c>HasData</c>), so it ships inside the migration, exists the moment the schema does,
/// and is also created by <c>EnsureCreated</c> for the InMemory test/dev provider.
/// </summary>
/// <remarks>
/// <c>HasData</c> needs fixed keys, so the id and concurrency stamp are constants. They are not
/// secrets and must never change - changing them would make EF try to delete and re-insert the role
/// (dropping every user's membership). Seeding it here, rather than creating it lazily at runtime
/// with <c>RoleManager</c>, means two simultaneous first requests can never race to create it.
/// </remarks>
public sealed class AdministratorRoleConfiguration : IEntityTypeConfiguration<IdentityRole>
{
    /// <summary>The Administrator role's fixed primary key.</summary>
    public const string AdministratorRoleId = "5c0f6a62-8b0b-4e0d-9a53-7a1d6f3b2c11";

    /// <summary>The Administrator role's fixed concurrency stamp.</summary>
    public const string AdministratorRoleConcurrencyStamp = "b3a8d1f4-2e6c-4f59-8a7b-0c9d5e4f6a22";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityRole> builder)
    {
        builder.HasData(new IdentityRole
        {
            Id = AdministratorRoleId,
            Name = RelioRoles.Administrator,
            NormalizedName = RelioRoles.Administrator.ToUpperInvariant(),
            ConcurrencyStamp = AdministratorRoleConcurrencyStamp,
        });
    }
}
