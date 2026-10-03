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
/// </remarks>
public sealed class RelioWebAppFactory : WebApplicationFactory<Program>
{
    private IHost? _realHost;

    /// <summary>
    /// The base address (e.g. <c>http://127.0.0.1:53412</c>) the app is actually listening on.
    /// Only valid once the host has started (after the first access that triggers host creation,
    /// e.g. <see cref="WebApplicationFactory{TEntryPoint}.Services"/>).
    /// </summary>
    public string ServerAddress { get; private set; } = string.Empty;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // NOT builder.ConfigureAppConfiguration: Relio.Web/Program.cs reads Database:Provider (via
        // AddRelioData) with a plain top-level statement, synchronously, before its own
        // `builder.Build()` call. WebApplicationFactory only splices ConfigureAppConfiguration's
        // extra sources in at that `Build()` call (via HostFactoryResolver), which is too late for
        // code that already ran earlier in Program.cs - the connection-string check would still
        // see the un-patched configuration and throw. Environment variables don't have this
        // problem: WebApplication.CreateBuilder(args) reads them as soon as it runs, which is
        // before any of Program.cs's own code executes, so setting the variable here - before
        // builder.Build() below triggers Program's Main - reaches it in time.
        Environment.SetEnvironmentVariable(
            ServiceCollectionExtensions.ProviderConfigurationKey.Replace(":", "__"),
            ServiceCollectionExtensions.InMemoryProvider);

        builder.ConfigureWebHost(webHostBuilder =>
        {
            // Port 0: ask the OS for any free loopback port, so parallel test runs never collide.
            webHostBuilder.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
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
