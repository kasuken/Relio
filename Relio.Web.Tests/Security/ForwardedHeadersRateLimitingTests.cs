using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Relio.Web.Security;

namespace Relio.Web.Tests.Security;

/// <summary>
/// Issue #160: behind a reverse proxy every request arrives from the proxy's address, so the per-address
/// rate limits only work per client when forwarded headers are trusted.
/// </summary>
public sealed class ForwardedHeadersRateLimitingTests
{
    private const string ProxyAddress = "10.0.0.7";

    [Fact]
    public async Task Without_forwarded_headers_every_client_behind_the_proxy_shares_one_budget()
    {
        using var host = await StartAsync(new Dictionary<string, string?>());

        (await RegisterAsync(host, "203.0.113.1")).Should().Be(HttpStatusCode.OK);
        (await RegisterAsync(host, "203.0.113.1")).Should().Be(HttpStatusCode.TooManyRequests);
        (await RegisterAsync(host, "203.0.113.2")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task With_forwarded_headers_each_client_address_has_its_own_budget()
    {
        using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["Security:ForwardedHeaders:Enabled"] = "true",
        });

        (await RegisterAsync(host, "203.0.113.1")).Should().Be(HttpStatusCode.OK);
        (await RegisterAsync(host, "203.0.113.1")).Should().Be(HttpStatusCode.TooManyRequests);
        (await RegisterAsync(host, "203.0.113.2")).Should().Be(HttpStatusCode.OK);
        (await RegisterAsync(host, "203.0.113.2")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Forwarded_client_addresses_with_a_port_are_accepted()
    {
        using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["Security:ForwardedHeaders:Enabled"] = "true",
        });

        // Azure App Service appends the client port to X-Forwarded-For.
        (await RegisterAsync(host, "203.0.113.1:50123")).Should().Be(HttpStatusCode.OK);
        (await RegisterAsync(host, "203.0.113.2:50124")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_peer_outside_the_known_proxies_cannot_choose_its_own_address()
    {
        using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["Security:ForwardedHeaders:Enabled"] = "true",
            ["Security:ForwardedHeaders:KnownProxies:0"] = "10.9.9.9",
        });

        (await RegisterAsync(host, "203.0.113.1")).Should().Be(HttpStatusCode.OK);
        (await RegisterAsync(host, "203.0.113.2")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_listed_proxy_network_is_trusted()
    {
        using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["Security:ForwardedHeaders:Enabled"] = "true",
            ["Security:ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/24",
        });

        (await RegisterAsync(host, "203.0.113.1")).Should().Be(HttpStatusCode.OK);
        (await RegisterAsync(host, "203.0.113.2")).Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Security:ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("Security:ForwardedHeaders:KnownNetworks:0", "10.0.0.0")]
    [InlineData("Security:ForwardedHeaders:ForwardLimit", "0")]
    public async Task Invalid_forwarded_header_configuration_fails_at_startup(string key, string value)
    {
        var act = () => StartAsync(new Dictionary<string, string?> { [key] = value });

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public void Forwarded_headers_are_off_by_default()
    {
        new ForwardedHeadersSettings().Enabled.Should().BeFalse();
    }

    private static async Task<IHost> StartAsync(Dictionary<string, string?> settings)
    {
        settings["Security:RateLimiting:RegistrationPermitLimit"] = "1";
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings));
                web.ConfigureServices((context, services) =>
                {
                    services.AddRelioForwardedHeaders(context.Configuration);
                    services.AddRelioAuthenticationRateLimiting(context.Configuration);
                });
                web.Configure(app =>
                {
                    // The test server has no real peer; stand in for the reverse proxy's connection.
                    app.Use((context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(ProxyAddress);
                        return next(context);
                    });
                    app.UseRelioForwardedHeaders();
                    app.UseRateLimiter();
                    app.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        return Task.CompletedTask;
                    });
                });
            })
            .Build();

        try
        {
            await host.StartAsync();
        }
        catch
        {
            host.Dispose();
            throw;
        }

        return host;
    }

    private static async Task<HttpStatusCode> RegisterAsync(IHost host, string forwardedFor)
    {
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Register");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
