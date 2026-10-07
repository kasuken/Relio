using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Relio.Application.Administration;
using Relio.Application.Reminders;
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
/// <item>#17 (password reset) gives password reset its own named token provider
/// (<see cref="PasswordResetTokenProvider{TUser}"/>, <see cref="PasswordResetTokenProviderName"/>)
/// instead of sharing email confirmation's "Default" one, so
/// <see cref="AccountOptions.PasswordReset"/>'s <c>TokenLifespan</c> can be configured
/// independently.</item>
/// <item>#19 (first-user admin + <c>Registration:Mode</c>) adds the Administrator role
/// (<c>AddRoles</c>), the <see cref="RelioPolicies.Administrator"/> policy, a custom
/// <see cref="RelioSignInManager"/> that refuses disabled accounts, the eagerly validated
/// <see cref="BuildRegistrationOptions"/>, and the configurable session validation interval
/// (<see cref="AccountSessionOptions"/>) - without touching password/email policy.</item>
/// <item>#20 (optional two-factor authentication with an authenticator app) needed no new Identity
/// options - <c>AddDefaultTokenProviders</c> already registers the authenticator token provider and
/// <see cref="RelioUser"/> already has the two-factor columns, from <see cref="IdentityUser"/> -
/// only the cookie hardening (<see cref="ApplyCookieSecurity"/> also covers the short-lived
/// two-factor cookies) and the <see cref="RelioSignInManager"/> overrides.</item>
/// </list>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The name <see cref="IdentityOptions.Tokens"/>'s <c>PasswordResetTokenProvider</c> is
    /// registered under (issue #17) - deliberately not <c>TokenOptions.DefaultProvider</c> (the
    /// "Default" provider email confirmation also uses), so
    /// <see cref="PasswordResetTokenProviderOptions.TokenLifespan"/> can be configured
    /// independently of email confirmation's. See <see cref="PasswordResetTokenProvider{TUser}"/>.
    /// </summary>
    private const string PasswordResetTokenProviderName = "PasswordReset";

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

        // Issue #19: parsed eagerly so a typo in Registration:Mode stops the app at startup (like
        // an unknown Database:Provider) instead of silently leaving sign-up open. Configured from
        // the already-validated values - not Configure(section), which would re-parse the raw text.
        var registrationOptions = BuildRegistrationOptions(configuration);
        services.Configure<RegistrationOptions>(options =>
        {
            options.Mode = registrationOptions.Mode;
            options.InvitationLifetime = registrationOptions.InvitationLifetime;
        });

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            // Issue #19. A UI/endpoint gate; IUserAdministrationService re-checks against the
            // database on every call, since a role claim only refreshes at sign-in.
            options.AddPolicy(RelioPolicies.Administrator, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(RelioRoles.Administrator));
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

                // Issue #17: password reset gets its own token provider/name (registered as a
                // transient service below), instead of staying on "Default" alongside email
                // confirmation - see PasswordResetTokenProviderName and
                // PasswordResetTokenProvider<TUser>'s remarks for why.
                options.Tokens.ProviderMap[PasswordResetTokenProviderName] =
                    new TokenProviderDescriptor(typeof(PasswordResetTokenProvider<RelioUser>));
                options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderName;
            })
            // Before AddEntityFrameworkStores: with no roles registered it wires up a user-only
            // store, and any role call (AddToRoleAsync, IsInRoleAsync) throws NotSupportedException.
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<RelioDbContext>()
            .AddSignInManager<RelioSignInManager>()
            .AddDefaultTokenProviders();

        // Issue #19: one knob for how quickly a revoked session (password changed, signed out
        // everywhere, account disabled) actually ends. The circuit revalidation reads the same
        // value from AccountOptions.
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = accountOptions.Session.ValidationInterval);

        services.AddTransient<PasswordResetTokenProvider<RelioUser>>();
        services.Configure<PasswordResetTokenProviderOptions>(options =>
        {
            options.TokenLifespan = accountOptions.PasswordReset.TokenLifespan;
        });

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

            ApplyCookieSecurity(options.Cookie, environment);
        });

        // Issue #20: the two cookies of the two-factor sign-in step get the same attributes as the
        // application cookie. "Identity.TwoFactorUserId" (5 minutes, Identity's default, kept) holds
        // the id of someone who passed the password check and is waiting to enter a code;
        // "Identity.TwoFactorRememberMe" is never issued (Relio has no "remember this device" - see
        // AGENTS.md) but is hardened anyway, so enabling it later cannot start from a weaker default.
        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.TwoFactorUserIdScheme, options => ApplyCookieSecurity(options.Cookie, environment));
        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.TwoFactorRememberMeScheme, options => ApplyCookieSecurity(options.Cookie, environment));

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        if (requireConfirmedAccount)
        {
            services.AddScoped<IEmailSender<RelioUser>, SmtpEmailSender>();
            services.AddScoped<IReminderEmailSender, SmtpReminderEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender<RelioUser>, NullEmailSender>();
            services.AddScoped<IReminderEmailSender, NullReminderEmailSender>();
        }

        return services;
    }

    /// <summary>
    /// Never readable from JavaScript (defence in depth against XSS exfiltrating the session) and
    /// never sent cross-site (SameSite=Lax still allows top-level navigation links, e.g. following
    /// an emailed link, unlike Strict). Secure is relaxed to SameAsRequest only in Development,
    /// where Relio.Web.E2ETests and local <c>dotnet run</c> serve plain HTTP on loopback with no
    /// HTTPS endpoint configured - requiring Secure there would mean the browser never sends the
    /// cookie back at all. Every other environment requires HTTPS (see Program.cs's
    /// UseHsts/UseHttpsRedirection), so Secure is always enforced there.
    /// </summary>
    private static void ApplyCookieSecurity(CookieBuilder cookie, IHostEnvironment environment)
    {
        cookie.HttpOnly = true;
        cookie.SameSite = SameSiteMode.Lax;
        cookie.SecurePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
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

    /// <summary>
    /// Reads <c>Registration:Mode</c> and <c>Registration:InvitationLifetime</c> (issue #19) into a
    /// <see cref="RegistrationOptions"/>. A missing or blank mode is <see cref="RegistrationMode.Open"/>
    /// (what Relio did before sign-up control existed); names are case-insensitive. Anything else -
    /// an unknown name, a number, or an undefined value - throws, so a misspelt
    /// <c>InviteOnyl</c> can never quietly leave the instance open. The invitation lifetime must be
    /// greater than zero.
    /// </summary>
    /// <exception cref="InvalidOperationException">The mode or lifetime is not valid.</exception>
    public static RegistrationOptions BuildRegistrationOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RegistrationOptions();

        var rawMode = configuration[$"{RegistrationOptions.SectionName}:Mode"];
        if (!string.IsNullOrWhiteSpace(rawMode))
        {
            var candidate = rawMode.Trim();

            // Enum.TryParse accepts numeric strings ("1") and any number for an undefined value
            // ("99"); only the names are a supported spelling, so reject everything else.
            if (candidate.All(char.IsLetter) && Enum.TryParse<RegistrationMode>(candidate, ignoreCase: true, out var mode)
                && Enum.IsDefined(mode))
            {
                options.Mode = mode;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unknown '{RegistrationOptions.SectionName}:Mode' value '{rawMode}'. Supported values are " +
                    $"'{nameof(RegistrationMode.Open)}' (the default), '{nameof(RegistrationMode.InviteOnly)}' " +
                    $"and '{nameof(RegistrationMode.Closed)}'.");
            }
        }

        var rawLifetime = configuration[$"{RegistrationOptions.SectionName}:InvitationLifetime"];
        if (!string.IsNullOrWhiteSpace(rawLifetime))
        {
            if (!TimeSpan.TryParse(rawLifetime, System.Globalization.CultureInfo.InvariantCulture, out var lifetime)
                || lifetime <= TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"Invalid '{RegistrationOptions.SectionName}:InvitationLifetime' value '{rawLifetime}'. " +
                    "It must be a time span greater than zero, for example '7.00:00:00' for 7 days.");
            }

            options.InvitationLifetime = lifetime;
        }

        return options;
    }
}
