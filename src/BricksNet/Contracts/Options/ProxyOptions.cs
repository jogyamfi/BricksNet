using System.Net;

namespace BricksNet.Contracts.Options;

/// <summary>Proxy configuration for outbound HTTP connections.</summary>
public sealed record ProxyOptions
{
    /// <summary>Proxy server address (e.g. <c>http://proxy.corp:8080</c> or <c>socks5://proxy.corp:1080</c>).</summary>
    public required Uri Uri { get; init; }

    /// <summary>Transport protocol of the proxy.</summary>
    public ProxyType Type { get; init; } = ProxyType.Http;

    /// <summary>Optional credentials for proxy authentication.</summary>
    public NetworkCredential? Credentials { get; init; }
}
