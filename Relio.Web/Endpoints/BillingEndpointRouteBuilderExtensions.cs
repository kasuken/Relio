using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Relio.Application.Billing;

namespace Relio.Web.Endpoints;

/// <summary>Maps the inbound billing webhook endpoint.</summary>
public static class BillingEndpointRouteBuilderExtensions
{
    /// <summary>The path to register as the Stripe webhook endpoint.</summary>
    public const string WebhookPath = "/api/webhooks/billing";

    /// <summary>The header Stripe signs its webhook payloads with.</summary>
    private const string StripeSignatureHeaderName = "Stripe-Signature";

    // Stripe events are a few kilobytes; anything far larger is not a Stripe delivery.
    private const long MaxPayloadBytes = 256 * 1024;

    /// <summary>
    /// Maps <c>POST /api/webhooks/billing</c>. Anonymous (Stripe has no Relio session) and without
    /// antiforgery (the Stripe signature is the proof of origin). All verification and state changes
    /// live in <see cref="IBillingWebhookProcessor"/>; this only reads the request and maps the result to
    /// a status code. Without a billing provider every delivery fails verification and gets 401.
    /// </summary>
    public static IEndpointRouteBuilder MapRelioBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
                WebhookPath,
                async (HttpContext context, IBillingWebhookProcessor processor, ILoggerFactory loggerFactory) =>
                {
                    // A machine endpoint: its status codes are the answer. Without this, the app's
                    // status code pages would re-execute the POST against the not-found page.
                    if (context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
                    {
                        statusCodePages.Enabled = false;
                    }

                    if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
                    {
                        sizeFeature.MaxRequestBodySize = MaxPayloadBytes;
                    }

                    if (context.Request.ContentLength > MaxPayloadBytes)
                    {
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }

                    string payload;
                    try
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        payload = await reader.ReadToEndAsync(context.RequestAborted);
                    }
                    catch (BadHttpRequestException)
                    {
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }

                    var signature = context.Request.Headers[StripeSignatureHeaderName].ToString();
                    var result = await processor.ProcessAsync(payload, signature, context.RequestAborted);
                    if (!result.Accepted)
                    {
                        loggerFactory.CreateLogger(typeof(BillingEndpointRouteBuilderExtensions))
                            .LogWarning("Billing webhook rejected: {Reason}.", result.Reason);
                    }

                    // Every accepted outcome (applied, a duplicate, stale, irrelevant, unknown user)
                    // returns 200 so the provider stops retrying. Only a failed signature is rejected.
                    return result.Accepted ? Results.Ok() : Results.Unauthorized();
                })
            .AllowAnonymous()
            .DisableAntiforgery();

        return endpoints;
    }
}
