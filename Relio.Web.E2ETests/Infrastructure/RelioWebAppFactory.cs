using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Relio.Data.DependencyInjection;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// Hosts the real Relio.Web app (<c>Program</c>, see its trailing <c>public partial class
/// Program;</c> marker) on a real Kestrel socket, configured for the EF Core InMemory database
/// provider, so Playwright can drive it like any other browser-visible site.
/// </summary>
/// <remarks>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> normally hosts the app behind an in-memory
/// <c>TestServer</c>, which Playwright cannot connect to (Playwright drives a real browser over a
/// real socket). Overriding <see cref="CreateHost"/> to build and <c>Start</c> the host directly
/// gives it a real, listening Kestrel endpoint instead, whose address is read back from
/// <see cref="IServerAddressesFeature"/> once the host is up.
///
/// To run a variant of the app (e.g. a different <c>Email:Provider</c> with a test email sink),
/// construct a *new* <c>RelioWebAppFactory(configureTestServices: ...)</c> rather than calling the
/// base class's <c>WithWebHostBuilder</c>: that method returns an internal
/// <c>DelegatedWebApplicationFactory</c> wrapper which still invokes *this* class's
/// <see cref="CreateHost"/> override on the *original* instance, overwriting its
/// <see cref="ServerAddress"/>/real host - exactly the shared fixture other tests depend on. A
/// fresh instance has its own independent real host, with no such risk.
/// </remarks>
public sealed class RelioWebAppFactory(Action<IServiceCollection>? configureTestServices = null)
    : WebApplicationFactory<Program>
{
    private IHost? _realHost;

    /// <summary>
    /// The base address (e.g. <c>http://127.0.0.1:53412</c>) the app is actually listening on.
    /// Only valid once the host has started (after the first access that triggers host creation,
    /// e.g. <see cref="WebApplicationFactory{TEntryPoint}.Services"/>).
    /// </summary>
    public string ServerAddress { get; private set; } = string.Empty;

    /// <summary>
    /// A new DI scope against the real, running app's service provider - not this factory's own
    /// <see cref="WebApplicationFactory{TEntryPoint}.Services"/>, which (see <see cref="CreateHost"/>'s
    /// remarks) belongs to a throwaway <c>TestServer</c>-backed host, not the real Kestrel one.
    /// Tests use this to assert through the real app's services (e.g. <c>RelioDbContext</c>,
    /// <c>UserManager&lt;RelioUser&gt;</c>) rather than only through the browser UI - e.g. proving
    /// a stored time zone or cross-user data isolation via the actual service, not a page.
    /// </summary>
    public IServiceScope CreateRealScope() =>
        (_realHost ?? throw new InvalidOperationException("The real host has not started yet."))
        .Services.CreateScope();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        using var protectionEnvironment =
            Relio.Web.Tests.Infrastructure.DataProtectionTestHarness.ConfigureHostEnvironment();

        // NOT builder.ConfigureAppConfiguration: Relio.Web/Program.cs reads Database:Provider (via
        // AddRelioData) with a plain top-level statement, synchronously, before its own
        // `builder.Build()` call. WebApplicationFactory only splices ConfigureAppConfiguration's
        // extra sources in at that `Build()` call (via HostFactoryResolver), which is too late for
        // code that already ran earlier in Program.cs - the connection-string check would still
        // see the un-patched configuration and throw. Environment variables don't have this
        // problem: WebApplication.CreateBuilder(args) reads them as soon as it runs, which is
        // before any of Program.cs's own code executes, so setting the variable here - before
        // builder.Build() below triggers Program's Main - reaches it in time.
        //
        // Only defaulted: a variant app (VariantApp, SqlServerPageLoadTests) sets
        // Database__Provider=SqlServer before building its host, because InMemory hides DbContext
        // concurrency bugs (see AGENTS.md "One database operation at a time").
        var providerVariable = ServiceCollectionExtensions.ProviderConfigurationKey.Replace(":", "__");
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(providerVariable)))
        {
            Environment.SetEnvironmentVariable(providerVariable, ServiceCollectionExtensions.InMemoryProvider);
        }

        // These synthetic test hosts share a loopback peer across many account flows; keep their
        // budgets explicitly high without changing the secure application defaults. Variants can
        // set lower values before Build (VariantApp restores these values afterwards).
        SetDefaultTestRateLimit("Security__RateLimiting__LoginPermitLimit", "100000");
        SetDefaultTestRateLimit("Security__RateLimiting__RegistrationPermitLimit", "100000");
        SetDefaultTestRateLimit("Security__RateLimiting__PasswordResetPermitLimit", "100000");

        // Same timing constraint as Database:Provider above - Program.cs reads this (via
        // DemoDataSeeder's options) before builder.Build(), so it must already be set as an
        // environment variable by the time this method's own builder.Build() call runs.
        Environment.SetEnvironmentVariable("DemoData__Enabled", "true");

        builder.ConfigureWebHost(webHostBuilder =>
        {
            // Port 0: ask the OS for any free loopback port, so parallel test runs never collide.
            webHostBuilder.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

            if (configureTestServices is not null)
            {
                webHostBuilder.ConfigureTestServices(configureTestServices);
            }
        });

        // Builds and starts the REAL app (Program.cs, full DI, real Kestrel socket) - this is
        // what Playwright and HealthEndpointTests actually talk to.
        _realHost = builder.Build();
        _realHost.Start();

        var addresses = _realHost.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("The Kestrel server did not expose its listening addresses.");
        ServerAddress = addresses.Addresses.First();

        // WebApplicationFactory's own StartServer() (invoked whenever anyone reads `Services`)
        // insists on casting the returned host's IServer to TestServer - true Kestrel fails that
        // cast. So it gets a second, throwaway, empty TestServer-backed host to satisfy that
        // bookkeeping instead; nothing in these tests reads this factory's own `Services` or
        // `CreateClient()` for anything meaningful; they use `ServerAddress`/`RelioAppFixture`
        // against the real host above.
        var dummyHost = new HostBuilder()
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder.UseTestServer();
                webHostBuilder.Configure(_ => { });
            })
            .Build();
        dummyHost.Start();

        return dummyHost;
    }

    private static void SetDefaultTestRateLimit(string name, string value)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>
    /// Disposes the real Kestrel host alongside the base class's own (throwaway) host. Always
    /// dispose this factory with <c>await</c> (<see cref="RelioAppFixture.DisposeAsync"/> does),
    /// not the synchronous <see cref="IDisposable.Dispose"/>.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_realHost is not null)
        {
            await _realHost.StopAsync();
            _realHost.Dispose();
        }
    }
}
