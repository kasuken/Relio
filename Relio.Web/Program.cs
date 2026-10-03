using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Relio.Data;
using Relio.Data.DependencyInjection;
using Relio.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddRelioData(builder.Configuration);

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
else
{
    // Production schema changes are an explicit, reviewed step (`dotnet ef database update`);
    // Development auto-applies pending migrations so the app always runs against the latest
    // schema.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<RelioDbContext>().Database.MigrateAsync();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Entry point partial, exposed so integration tests can host Relio.Web in-memory.</summary>
public partial class Program;
