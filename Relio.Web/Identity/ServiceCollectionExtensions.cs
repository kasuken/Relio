using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Web.Email;

namespace Relio.Web.Identity;

/// <summary>
/// Registers ASP.NET Core Identity for Relio (epic #14). All Identity options live here, in one
/// place, so later issues extend them rather than scattering configuration:
/// <list type="bullet">
/// <item>#16 (login/logout/lockout/remember-me) tunes <c>IdentityOptions.Lockout</c> and the
/// application cookie's <c>ExpireTimeSpan</c>/persistence.</item>
/// <item>#19 (first-user admin + <c>Registration:Mode</c>) adds a role and gates registration
/// without touching password/email policy.</item>
/// <item>#20 (2FA) turns on <c>IdentityOptions.Tokens</c>/<c>SignIn.RequireConfirmedPhoneNumber</c>-style
/// options and a new login step; <see cref="RelioUser"/> already has the two-factor columns it
/// needs, from <see cref="IdentityUser"/>.</item>
/// </list>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ASP.NET Core Identity, cookie authentication, a fallback "authenticated user required"
    /// authorization policy (account pages and health checks opt out explicitly with
    /// <c>[AllowAnonymous]</c>/<c>.AllowAnonymous()</c>), and the email sender selected by
    /// <c>Email:Provider</c> (see <see cref="EmailOptions"/>).
    /// </summary>
    public static IServiceCollection AddRelioIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var requireConfirmedAccount = RequiresConfirmedAccount(configuration);

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddIdentityCore<RelioUser>(options =>
            {
                // Length over complexity is the stronger lever (NIST SP 800-63B); Relio also asks
                // for a mix of character classes on top, for defence in depth - this stores
                // private relationship data, so 12 is deliberately above Identity's 6-character
                // default.
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;

                options.User.RequireUniqueEmail = true;

                // Only meaningful with an email provider configured (Email:Provider=Smtp) - with
                // no provider, nobody could ever confirm an account, so sign-up would be a dead
                // end (epic #14's guardrail: email features degrade gracefully with none
                // configured).
                options.SignIn.RequireConfirmedAccount = requireConfirmedAccount;
            })
            .AddEntityFrameworkStores<RelioDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.SlidingExpiration = true;
        });

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        if (requireConfirmedAccount)
        {
            services.AddScoped<IEmailSender<RelioUser>, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender<RelioUser>, NullEmailSender>();
        }

        return services;
    }

    /// <summary>
    /// Whether account emails are required to be confirmed before sign-in: <see langword="true"/>
    /// only when <c>Email:Provider</c> is <see cref="EmailOptions.SmtpProvider"/>.
    /// </summary>
    public static bool RequiresConfirmedAccount(IConfiguration configuration) =>
        string.Equals(
            configuration[$"{EmailOptions.SectionName}:Provider"],
            EmailOptions.SmtpProvider,
            StringComparison.OrdinalIgnoreCase);
}
