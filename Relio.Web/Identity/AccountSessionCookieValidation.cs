using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data;

namespace Relio.Web.Identity;

/// <summary>
/// Adds a fresh account-existence/disabled check to every authenticated HTTP cookie request,
/// without replacing Identity's periodic security-stamp validation.
/// </summary>
public static class AccountSessionCookieValidation
{
    /// <summary>Registers cookie validation that promptly rejects erased or disabled accounts.</summary>
    /// <param name="services">The dependency-injection services.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAccountSessionCookieValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.PostConfigure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme,
            options =>
            {
                var existingValidation = options.Events.OnValidatePrincipal;
                options.Events.OnValidatePrincipal = async context =>
                {
                    var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!string.IsNullOrEmpty(userId))
                    {
                        var dbContext = context.HttpContext.RequestServices
                            .GetRequiredService<RelioDbContext>();
                        var isDisabled = await dbContext.Users
                            .AsNoTracking()
                            .Where(user => user.Id == userId)
                            .Select(user => (bool?)user.IsDisabled)
                            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                        if (isDisabled is null or true)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                            return;
                        }
                    }

                    if (existingValidation is not null)
                    {
                        await existingValidation(context);
                    }
                };
            });

        return services;
    }
}
