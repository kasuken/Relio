using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data.Seeding;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// A second, independent running copy of the app (its own Kestrel port and its own InMemory
/// database) with different configuration, for tests of issue #19 that need a particular
/// <c>Registration:Mode</c>, a fresh instance with no accounts at all, or a short session
/// validation interval. The shared fixture app cannot serve those: it always has the demo account
/// (so no registered account is ever "first") and runs in the default Open mode.
/// </summary>
/// <remarks>
/// Settings that Program.cs reads while the host is being built (<c>Registration:Mode</c>,
/// <c>Account:Session:ValidationInterval</c>, ...) must be environment variables set before
/// <c>RelioWebAppFactory</c> builds its host - the same constraint and the same remedy as
/// <see cref="AccountTestHelpers.CreateSmtpFactory"/>. They are process-wide, which is safe only
/// because every test class in <see cref="RelioAppCollection"/> runs serially, and they are reset
/// as soon as the host exists. Dispose with <c>await using</c>: it closes every page it opened and
/// stops the extra host.
/// </remarks>
public sealed class VariantApp : IAsyncDisposable
{
    private readonly RelioAppFixture _fixture;
    private readonly List<IBrowserContext> _contexts = [];

    private VariantApp(RelioAppFixture fixture, RelioWebAppFactory factory)
    {
        _fixture = fixture;
        Factory = factory;
    }

    /// <summary>The variant app's factory, for <see cref="RelioWebAppFactory.CreateRealScope"/>.</summary>
    public RelioWebAppFactory Factory { get; }

    /// <summary>
    /// Starts a variant app.
    /// </summary>
    /// <param name="fixture">The shared fixture, for its browser.</param>
    /// <param name="environment">
    /// Extra configuration as environment-variable style keys, e.g. <c>["Registration__Mode"] = "Closed"</c>.
    /// </param>
    /// <param name="seedDemoData">
    /// <see langword="false"/> for an instance with no accounts at all.
    /// <c>RelioWebAppFactory</c> always sets <c>DemoData__Enabled=true</c>, so this turns it off
    /// through the options pipeline instead of the environment.
    /// </param>
    public static VariantApp Create(
        RelioAppFixture fixture,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool seedDemoData = true)
    {
        var variables = environment ?? new Dictionary<string, string?>();
        foreach (var (key, value) in variables)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            var factory = new RelioWebAppFactory(services =>
            {
                if (!seedDemoData)
                {
                    services.PostConfigure<DemoDataOptions>(options => options.Enabled = false);
                }
            });

            // Forces CreateHost to actually run while the environment variables above are set.
            _ = factory.Services;
            return new VariantApp(fixture, factory);
        }
        finally
        {
            foreach (var key in variables.Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }

    /// <summary>A fresh browser context (own cookies) and page pointed at this variant app.</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Factory.ServerAddress,
            ViewportSize = Viewports.Desktop,
        });
        _contexts.Add(context);
        return await context.NewPageAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.CloseAsync();
        }

        await Factory.DisposeAsync();
    }
}
