using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using MudBlazor.Services;
using Relio.Application.Security;
using Relio.Data;
using Relio.Data.DependencyInjection;
using Relio.Data.Seeding;
using Relio.Web.Components;
using Relio.Web.Components.Account;
using Relio.Web.Identity;
using Relio.Web.Security;
using Relio.Web.Time;
using Relio.Web.Theme;
using DataServiceCollectionExtensions = Relio.Data.DependencyInjection.ServiceCollectionExtensions;
using IdentityServiceCollectionExtensions = Relio.Web.Identity.ServiceCollectionExtensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Calm, consistent snackbar behaviour (see docs/design-system/README.md): bottom of the
// screen, one at a time, closeable, never stacking duplicate messages.
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
    config.SnackbarConfiguration.PreventDuplicates = true;
    config.SnackbarConfiguration.NewestOnTop = false;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
    config.SnackbarConfiguration.HideTransitionDuration = 250;
    config.SnackbarConfiguration.ShowTransitionDuration = 250;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
});

// Circuit-scoped light/dark preference shared by MainLayout, the app bar's theme menu and
// Settings (see Theme/ThemeModeState.cs). Per-browser only until accounts (epic #14) land.
builder.Services.AddScoped<IThemeModeStore, JsThemeModeStore>();
builder.Services.AddScoped<ThemeModeState>();

builder.Services.AddRelioData(builder.Configuration);

// ASP.NET Core Identity (epic #14): local accounts, password policy, cookie auth and the
// email sender selected by Email:Provider. See Relio.Web.Identity.ServiceCollectionExtensions
// for every Identity option Relio sets - #16-#20 extend them there, not in Program.cs.
builder.Services.AddCascadingAuthenticationState();

// Issue #16: a connected circuit otherwise only ever sees the principal captured once, when it
// was created - this makes it periodically re-check the user's security stamp, so a session
// revoked mid-connection (signed out everywhere, password changed) is actually noticed. Replaces
// (not adds to) the cascading registration above - see the provider's own remarks.
builder.Services.AddScoped<AuthenticationStateProvider, RelioRevalidatingAuthenticationStateProvider>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddRelioIdentity(builder.Configuration, builder.Environment);

// ICurrentUser is the only way Application services read the signed-in user; it never depends
// on HttpContext directly (see the "User-scoped data pattern" section of AGENTS.md).
// AuthenticationStateCurrentUser (not HttpContext-backed) is what makes this keep working once a
// Blazor Server circuit's SignalR connection takes over from the initial HTTP request - see its
// own remarks.
builder.Services.AddScoped<ICurrentUser, AuthenticationStateCurrentUser>();

// Reads the browser's IANA time zone via JS interop, so account settings (#18) can suggest it
// (never silently saving it) in the interactive TimeZoneSettings component. Sign-up (#15) itself
// cannot use JS interop - see Components/Account/Pages/Register.razor.
builder.Services.AddScoped<IBrowserTimeZoneReader, BrowserTimeZoneReader>();

// "live" answers whether the process is up; "ready" also covers the database so load
// balancers and the shared release workflow (which smoke-tests /health/ready) know when
// Relio can actually serve requests.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<RelioDbContext>("database", tags: ["ready"]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (!IdentityServiceCollectionExtensions.RequiresConfirmedAccount(builder.Configuration))
{
    // Logged once at startup, not per-registration, so it is visible without being noisy (see
    // Relio.Web.Email.NullEmailSender for the per-send warning).
    app.Logger.LogWarning(
        "Email:Provider is not set to 'Smtp': Relio will not send any account emails (confirmation, " +
        "password reset). New accounts do not require email confirmation. This is expected for a " +
        "self-hosted instance with no email provider configured (epic #14's guardrail); set " +
        "Email:Provider=Smtp and Email:Smtp:* to enable them.");
}

if (DataServiceCollectionExtensions.IsInMemoryProvider(builder.Configuration))
{
    // Database:Provider=InMemory is test/dev only (see AGENTS.md "End-to-end tests" and
    // ServiceCollectionExtensions.AddRelioData): no migrations exist for it, so the schema is
    // just created from the current model. Logged loudly so nobody mistakes this for a real
    // deployment.
    app.Logger.LogWarning(
        "Relio is running with the EF Core InMemory database provider ({ProviderKey}={ProviderValue}). " +
        "This is a test/dev-only configuration: it is not durable, does not run migrations and does " +
        "not enforce SQL Server constraints. Never use it in a hosted or self-hosted deployment.",
        DataServiceCollectionExtensions.ProviderConfigurationKey,
        DataServiceCollectionExtensions.InMemoryProvider);

    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<RelioDbContext>().Database.EnsureCreatedAsync();
}
else if (app.Environment.IsDevelopment())
{
    // Production schema changes are an explicit, reviewed step (`dotnet ef database update`);
    // Development auto-applies pending migrations so the app always runs against the latest
    // schema.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<RelioDbContext>().Database.MigrateAsync();
}

// DemoDataSeeder itself refuses (logging an error) when the environment is Production, and does
// nothing when DemoData:Enabled is not true - see its own remarks. Always runs after the schema
// is ready (above) and is safe to run on every startup (idempotent).
using (var seedScope = app.Services.CreateScope())
{
    await seedScope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Issue #16: "the back button doesn't reveal data" after signing out. context.User is populated
// by UseAuthentication above, so this must run after it; it must run before anything writes the
// response body, so the header is queued via OnStarting rather than set directly here. Every
// authenticated response - not just the account pages - gets this: Relio has no page a signed-in
// user would want a shared/forward cache (CDN, browser back/forward cache) to retain after they
// sign out, e.g. on a shared computer.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store, no-cache";
            context.Response.Headers.Pragma = "no-cache";
        }

        return Task.CompletedTask;
    });

    await next();
});

app.UseAntiforgery();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.MapRelioIdentityEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Entry point partial, exposed so integration tests can host Relio.Web in-memory.</summary>
public partial class Program;
