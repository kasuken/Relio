namespace Relio.Web.Security;

/// <summary>Configures trust in <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from a reverse proxy.</summary>
/// <remarks>
/// Off by default: a client that reaches the app directly could otherwise choose its own address and
/// dodge per-address rate limits. Turn it on only when every request arrives through a proxy you
/// control (Azure App Service front ends, Caddy, nginx, a load balancer). <c>X-Forwarded-Host</c> is
/// never honoured.
/// </remarks>
public sealed class ForwardedHeadersSettings
{
    /// <summary>The configuration section containing these options.</summary>
    public const string SectionName = "Security:ForwardedHeaders";

    /// <summary>Whether forwarded headers are processed.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Proxy addresses allowed to set forwarded headers. When both this and <see cref="KnownNetworks"/>
    /// are empty, any direct peer is trusted, which is only safe when the app is not reachable except
    /// through the proxy (the Azure App Service case, where front-end addresses are not stable).
    /// </summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>Proxy networks in CIDR notation (for example <c>10.0.0.0/8</c>) allowed to set forwarded headers.</summary>
    public List<string> KnownNetworks { get; set; } = [];

    /// <summary>The number of proxies in the chain whose forwarded values are consumed.</summary>
    public int ForwardLimit { get; set; } = 1;
}
