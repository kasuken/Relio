using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Relio.Web.Security;

/// <summary>Registers Relio's peer-based rate limits for sensitive account form posts.</summary>
public static class RateLimitingServiceCollectionExtensions
{
    private const string RejectedMessage = "Too many attempts. Please wait before trying again.";

    /// <summary>
    /// Registers validated fixed-window limits for login, second-factor, account-deletion
    /// reauthentication, registration, and password-recovery <c>POST</c> requests. Partitions are
    /// process-salted hashes of the connection peer address; request bodies, account identifiers,
    /// and forwarded headers are never read.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRelioAuthenticationRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();
        services.AddSingleton<RateLimitPeerPartitionKeyFactory>();
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var settings = httpContext.RequestServices
                    .GetRequiredService<IOptions<RateLimitingOptions>>()
                    .Value;
                if (!settings.Enabled
                    || !AuthenticationRateLimitPolicy.TryCreate(httpContext.Request, settings, out var policy))
                {
                    return RateLimitPartition.GetNoLimiter<string>("not-limited");
                }

                var partitionKey = httpContext.RequestServices
                    .GetRequiredService<RateLimitPeerPartitionKeyFactory>()
                    .Create(policy.Name, httpContext.Connection.RemoteIpAddress);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = policy.PermitLimit,
                        Window = policy.Window,
                        QueueLimit = RateLimitingOptions.QueueLimit,
                        AutoReplenishment = true,
                    });
            });
            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext rejection,
        CancellationToken cancellationToken)
    {
        var context = rejection.HttpContext;
        var response = context.Response;
        var settings = context.RequestServices
            .GetRequiredService<IOptions<RateLimitingOptions>>()
            .Value;
        var retrySeconds = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter.TotalSeconds
            : AuthenticationRateLimitPolicy.TryCreate(context.Request, settings, out var policy)
                ? policy.Window.TotalSeconds
                : 60;

        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retrySeconds))
            .ToString(CultureInfo.InvariantCulture);
        response.Headers.CacheControl = "no-store";
        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync(RejectedMessage, cancellationToken);
    }
}

internal readonly record struct AuthenticationRateLimitPolicy(
    string Name,
    int PermitLimit,
    TimeSpan Window)
{
    public static bool TryCreate(
        HttpRequest request,
        RateLimitingOptions options,
        out AuthenticationRateLimitPolicy policy)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            policy = default;
            return false;
        }

        var path = request.Path.Value;

        if (IsPath(path, "/Account/Login")
            || IsPath(path, "/Account/LoginWith2fa")
            || IsPath(path, "/Account/LoginWithRecoveryCode")
            || IsPath(path, "/Account/Manage/DeleteAccount"))
        {
            policy = new("login", options.LoginPermitLimit, options.LoginWindow);
            return true;
        }

        if (IsPath(path, "/Account/Register"))
        {
            policy = new("registration", options.RegistrationPermitLimit, options.RegistrationWindow);
            return true;
        }

        if (IsPath(path, "/Account/ForgotPassword")
            || IsPath(path, "/Account/ResetPassword"))
        {
            policy = new("password-reset", options.PasswordResetPermitLimit, options.PasswordResetWindow);
            return true;
        }

        policy = default;
        return false;
    }

    private static bool IsPath(string? actual, string expected)
    {
        if (actual is null || !actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Accept only the canonical path followed by slashes; never match a route-name prefix.
        for (var index = expected.Length; index < actual.Length; index++)
        {
            if (actual[index] != '/')
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class RateLimitPeerPartitionKeyFactory
{
    private readonly byte[] _processSalt;

    public RateLimitPeerPartitionKeyFactory() =>
        _processSalt = RandomNumberGenerator.GetBytes(32);

    public string Create(string policyName, IPAddress? remoteAddress)
    {
        var addressBytes = remoteAddress?.GetAddressBytes() ?? [];
        var digest = HMACSHA256.HashData(_processSalt, addressBytes);
        return $"{policyName}:{Convert.ToHexString(digest)}";
    }
}
