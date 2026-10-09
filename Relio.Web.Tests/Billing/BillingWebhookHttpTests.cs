using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Relio.Application.Billing;
using Relio.Data;
using Relio.Data.Identity;
using Relio.Data.Seeding;
using Relio.Web.Endpoints;
using Relio.Web.Tests.Infrastructure;
using Relio.Web.Tests.Marketing;

namespace Relio.Web.Tests.Billing;

/// <summary>
/// The real webhook endpoint over real HTTP: anonymous, exempt from antiforgery, 401 for anything
/// the provider does not verify, and 200 with the plan applied for a verified event.
/// </summary>
[Collection(MarketingHttpCollection.Name)]
public sealed class BillingWebhookHttpTests
{
    [Fact]
    public async Task Without_a_billing_provider_every_delivery_is_refused_without_a_redirect_or_antiforgery_error()
    {
        await using var factory = CreateFactory(provider: null);
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(
            BillingEndpointRouteBuilderExtensions.WebhookPath,
            new StringContent("{\"id\":\"evt_1\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_verified_event_is_applied_and_acknowledged()
    {
        var provider = new VerifyingProvider();
        await using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);
        var userId = Guid.NewGuid().ToString();
        using (var scope = factory.CreateRealScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
            dbContext.Users.Add(new RelioUser { Id = userId, UserName = $"{userId}@example.com", Email = $"{userId}@example.com" });
            await dbContext.SaveChangesAsync();
        }

        provider.Event = new ParsedBillingEvent(
            "evt_http_1", "customer.subscription.updated", userId, PlanTier.Pro, null,
            BillingProviderCustomerId: "cus_http", OccurredAtUtc: DateTime.UtcNow);
        using var request = new HttpRequestMessage(HttpMethod.Post, BillingEndpointRouteBuilderExtensions.WebhookPath)
        {
            Content = new StringContent("{\"id\":\"evt_http_1\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", VerifyingProvider.ValidSignature);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.ReceivedPayloads.Should().Equal("{\"id\":\"evt_http_1\"}");
        using var verify = factory.CreateRealScope();
        var stored = await verify.ServiceProvider.GetRequiredService<RelioDbContext>().UserSubscriptions
            .AsNoTracking().SingleAsync(row => row.UserId == userId);
        stored.Tier.Should().Be(PlanTier.Pro);
    }

    [Fact]
    public async Task An_oversized_payload_is_refused_before_it_is_verified()
    {
        var provider = new VerifyingProvider();
        await using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, BillingEndpointRouteBuilderExtensions.WebhookPath)
        {
            Content = new StringContent(new string('x', 300 * 1024), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", VerifyingProvider.ValidSignature);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        provider.ReceivedPayloads.Should().BeEmpty();
    }

    private static RelioWebAppFactory CreateFactory(IBillingProvider? provider)
    {
        var factory = new RelioWebAppFactory(services =>
        {
            services.PostConfigure<DemoDataOptions>(options => options.Enabled = false);
            if (provider is not null)
            {
                services.RemoveAll<IBillingProvider>();
                services.AddSingleton(provider);
            }
        });
        _ = factory.Services;
        return factory;
    }

    private static HttpClient CreateClient(RelioWebAppFactory factory) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(factory.ServerAddress) };

    /// <summary>Accepts exactly one signature value and returns the event it was given.</summary>
    private sealed class VerifyingProvider : IBillingProvider
    {
        public const string ValidSignature = "t=1,v1=valid-for-tests";

        public ParsedBillingEvent? Event { get; set; }

        public List<string> ReceivedPayloads { get; } = [];

        public bool IsEnabled => true;

        public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default)
        {
            ReceivedPayloads.Add(payload);
            var valid = signatureHeader == ValidSignature;
            return Task.FromResult(new WebhookVerificationResult(valid, valid ? null : "bad signature"));
        }

        public Task<ParsedBillingEvent?> ParseWebhookEventAsync(string payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(Event);

        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(BillingCheckoutRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ParsedBillingEvent?> GetCompletedCheckoutAsync(string userId, string checkoutSessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PortalSessionResult> CreatePortalSessionAsync(string customerId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateCustomerEmailAsync(string customerId, string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionCancellationResult> CancelSubscriptionsAsync(string customerId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
