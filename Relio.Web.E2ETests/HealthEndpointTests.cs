using System.Net;
using Relio.Web.E2ETests.Infrastructure;

namespace Relio.Web.E2ETests;

[Collection(RelioAppCollection.Name)]
public class HealthEndpointTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Health_live_returns_200()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ready_returns_200()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // RelioWebAppFactory.CreateHost starts a real Kestrel host (so Playwright can reach it), not
    // the in-memory TestServer the factory's own CreateClient()/CreateDefaultClient() expect -
    // calling those here would throw. A plain HttpClient against the real base address works for
    // the health endpoints, which don't need a browser.
    private HttpClient CreateClient() => new() { BaseAddress = new Uri(fixture.BaseUrl) };
}
