using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data.Identity;

namespace Relio.Data.Tests.Seeding;

/// <summary>
/// Builds a real <see cref="UserManager{TUser}"/> for <see cref="RelioUser"/> against a given
/// <see cref="RelioDbContext"/>, for tests that need actual Identity behaviour (password hashing,
/// unique email) rather than a hand-rolled fake. Mirrors the minimal set of services
/// <c>Relio.Web.Identity.ServiceCollectionExtensions.AddRelioIdentity</c> registers in the real
/// app.
/// </summary>
internal static class UserManagerTestFactory
{
    public static UserManager<RelioUser> Create(RelioDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentityCore<RelioUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.User.RequireUniqueEmail = true;
            })
            // Before the stores (like AddRelioIdentity): otherwise role calls throw NotSupportedException.
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<RelioDbContext>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider().GetRequiredService<UserManager<RelioUser>>();
    }
}
