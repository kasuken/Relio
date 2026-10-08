using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Billing;

namespace Relio.Web.Billing;

/// <summary>
/// Live implementation of <see cref="IBillingProvider"/> for Stripe.
/// Communicates with Stripe REST API over HTTPS using standard .NET HTTP client.
/// </summary>
public sealed class StripeBillingProvider : IBillingProvider
{
    private const string StripeApiBaseUrl = "https://api.stripe.com/v1/";
    private readonly HttpClient _httpClient;
    private readonly BillingOptions _options;
    private readonly ILogger<StripeBillingProvider> _logger;

    public StripeBillingProvider(
        HttpClient httpClient,
        IOptions<BillingOptions> options,
        ILogger<StripeBillingProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(StripeApiBaseUrl);
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public bool IsBillingEnabled => _options.IsStripeFullyConfigured;

    public BillingProviderType ProviderType => BillingProviderType.Stripe;

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId,
        string priceId,
        string? accountEmail = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsStripeFullyConfigured)
        {
            return new CheckoutSessionResult(false, null, "Stripe billing is not fully configured.");
        }

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("mode", "subscription"),
            new("line_items[0][price]", priceId),
            new("line_items[0][quantity]", "1"),
            new("client_reference_id", userId),
            new("success_url", _options.CheckoutSuccessUrl ?? "https://localhost"),
            new("cancel_url", _options.CheckoutCancelUrl ?? "https://localhost"),
        };

        if (!string.IsNullOrWhiteSpace(accountEmail))
        {
            parameters.Add(new("customer_email", accountEmail));
        }

        try
        {
            using var content = new FormUrlEncodedContent(parameters);
            using var response = await _httpClient.PostAsync("checkout/sessions", content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Stripe checkout session creation failed with status code {StatusCode}.", response.StatusCode);
                return new CheckoutSessionResult(false, null, "Failed to create checkout session.");
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("url", out var urlElement))
            {
                var sessionUrl = urlElement.GetString();
                return new CheckoutSessionResult(true, sessionUrl);
            }

            return new CheckoutSessionResult(false, null, "No session URL in response.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating Stripe checkout session.");
            return new CheckoutSessionResult(false, null, "An error occurred while contacting the billing provider.");
        }
    }

    public async Task<PortalSessionResult> CreatePortalSessionAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsStripeFullyConfigured || string.IsNullOrWhiteSpace(_options.PortalReturnUrl))
        {
            return new PortalSessionResult(false, null, "Stripe customer portal is not configured.");
        }

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("customer", customerId),
            new("return_url", _options.PortalReturnUrl),
        };

        try
        {
            using var content = new FormUrlEncodedContent(parameters);
            using var response = await _httpClient.PostAsync("billing_portal/sessions", content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Stripe portal session creation failed with status code {StatusCode}.", response.StatusCode);
                return new PortalSessionResult(false, null, "Failed to create customer portal session.");
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("url", out var urlElement))
            {
                var portalUrl = urlElement.GetString();
                return new PortalSessionResult(true, portalUrl);
            }

            return new PortalSessionResult(false, null, "No portal URL in response.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating Stripe customer portal session.");
            return new PortalSessionResult(false, null, "An error occurred while contacting the billing provider.");
        }
    }

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSigningSecret) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return Task.FromResult(new WebhookVerificationResult(false, "Webhook signing secret or header is missing."));
        }

        try
        {
            var headerItems = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string? timestamp = null;
            string? signature = null;

            foreach (var item in headerItems)
            {
                var parts = item.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                if (parts[0] == "t")
                {
                    timestamp = parts[1];
                }
                else if (parts[0] == "v1")
                {
                    signature = parts[1];
                }
            }

            if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature))
            {
                return Task.FromResult(new WebhookVerificationResult(false, "Invalid signature header format."));
            }

            var signedPayload = $"{timestamp}.{payload}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSigningSecret));
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
            var computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();

            var isValid = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHex),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));

            return Task.FromResult(new WebhookVerificationResult(isValid, isValid ? null : "Signature mismatch."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error verifying Stripe webhook signature.");
            return Task.FromResult(new WebhookVerificationResult(false, "Verification error."));
        }
    }
}
