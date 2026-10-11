using Stripe;

namespace Relio.Web.Tests.Billing;

/// <summary>One request <see cref="FakeStripeClient"/> received.</summary>
internal sealed record FakeStripeRequest(HttpMethod Method, string Path, Type ResponseType, BaseOptions Options);

/// <summary>
/// Test double for <see cref="IStripeClient"/> that never makes a network call: it records every
/// request and hands back a caller-supplied canned response (or throws the caller's exception), so
/// <see cref="Relio.Web.Billing.StripeBillingProvider"/> can be exercised end to end without ever
/// reaching the real Stripe API.
/// </summary>
internal sealed class FakeStripeClient : IStripeClient
{
    private readonly Func<FakeStripeRequest, IStripeEntity> _respond;

    /// <summary>Responds by the requested entity type only.</summary>
    public FakeStripeClient(Func<Type, IStripeEntity> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        _respond = request => respond(request.ResponseType);
    }

    private FakeStripeClient(Func<FakeStripeRequest, IStripeEntity> respond, bool perRequest)
    {
        _ = perRequest;
        _respond = respond;
    }

    /// <summary>Responds per request, so a test can answer (or fail) one path differently from another.</summary>
    public static FakeStripeClient PerRequest(Func<FakeStripeRequest, IStripeEntity> respond) => new(respond, perRequest: true);

    /// <summary>A client whose every call fails the test: for paths that must never reach Stripe.</summary>
    public static FakeStripeClient NeverCalled() =>
        PerRequest(request => throw new InvalidOperationException($"Stripe must not be called ({request.Method} {request.Path})."));

    public List<FakeStripeRequest> Requests { get; } = [];

    /// <summary>The options object sent with each request, so tests can assert what Stripe was actually asked for.</summary>
    public IEnumerable<BaseOptions> SentOptions => Requests.Select(request => request.Options);

    public string ApiKey => "sk_test_fake_key_never_sent_anywhere";
    public string? ClientId => null;
    public string ApiBase => "https://api.stripe.com";
    public string ConnectBase => "https://connect.stripe.com";
    public string FilesBase => "https://files.stripe.com";
    public string MeterEventsBase => "https://meter-events.stripe.com";

    public Task<T> RequestAsync<T>(
        HttpMethod method,
        string path,
        BaseOptions options,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
        where T : IStripeEntity
    {
        var request = new FakeStripeRequest(method, path, typeof(T), options);
        Requests.Add(request);
        return Task.FromResult((T)_respond(request));
    }

    public Task<Stream> RequestStreamingAsync(
        HttpMethod method,
        string path,
        BaseOptions options,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by any code path under test.");
}
