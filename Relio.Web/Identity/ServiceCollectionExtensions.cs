using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Web.Email;

namespace Relio.Web.Identity;

/// <summary>
/// Registers ASP.NET Core Identity for Relio (epic #14). All Identity options live here, in one
/// place, so later issues extend them rather than scattering configuration:
/// <list type="bullet">
/// <item>#16 (login/logout/lockout/remember-me) tunes <c>IdentityOptions.Lockout</c> and the
/// application cookie's <c>ExpireTimeSpan</c>/persistence/security attributes - see
/// <see cref="AccountOptions"/> for the configurable values.</item>
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
    /// <param name="environment">
    /// Used only to relax the sign-in cookie's <c>Secure</c> policy in Development (see the
    /// <c>ConfigureApplicationCookie</c> call below) - every other option is the same in every
    /// environment.
    /// </param>
    public static IServiceCollection AddRelioIdentity(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var requireConfirmedAccount = RequiresConfirmedAccount(configuration);
        var accountOptions = BuildAccountOptions(configuration);
        services.Configure<AccountOptions>(configuration.GetSection(AccountOptions.SectionName));

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

                // Issue #16: lock an account out after repeated failed sign-ins, for every
                // account from the moment it is created (AllowedForNewUsers) - see
                // AccountLockoutOptions for the configurable values and their safe defaults.
                options.Lockout.MaxFailedAccessAttempts = accountOptions.Lockout.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = accountOptions.Lockout.DefaultLockoutTimeSpan;
                options.Lockout.AllowedForNewUsers = accountOptions.Lockout.AllowedForNewUsers;
            })
            .AddEntityFrameworkStores<RelioDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";

            // Sliding: a session that is still active gets its expiry pushed forward, so an
            // active user is never signed out mid-use, while an abandoned one still expires.
            // ExpireTimeSpan is the ticket's lifetime either way - what "Remember me" (isPersistent,
            // set per sign-in in Login.razor) actually controls is whether the cookie the browser
            // receives is persistent (survives closing the browser) or a plain session cookie
            // (deleted when the browser closes) - see AccountCookieOptions.
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = accountOptions.Cookie.ExpireTimeSpan;

            // Never readable from JavaScript (defence in depth against XSS exfiltrating the
            // session) and never sent cross-site (SameSite=Lax still allows top-level navigation
            // links, e.g. following an emailed link, unlike Strict). Secure is relaxed to
            // SameAsRequest only in Development, where Relio.Web.E2ETests and local `dotnet run`
            // serve plain HTTP on loopback with no HTTPS endpoint configured - requiring Secure
            // there would mean the browser never sends the cookie back at all. Every other
            // environment requires HTTPS (see Program.cs's UseHsts/UseHttpsRedirection), so Secure
            // is always enforced there.
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
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

    /// <summary>
    /// Binds the <c>Account</c> configuration section into an <see cref="AccountOptions"/>,
    /// falling back to its defaults when the section (or any part of it) is absent. Exposed as a
    /// plain function (not just <c>services.Configure&lt;AccountOptions&gt;</c>) so option parsing
    /// can be unit tested without building a full <see cref="IServiceCollection"/>, the same way
    /// <see cref="RequiresConfirmedAccount"/> is.
    /// </summary>
    public static AccountOptions BuildAccountOptions(IConfiguration configuration) =>
        configuration.GetSection(AccountOptions.SectionName).Get<AccountOptions>() ?? new AccountOptions();
}
