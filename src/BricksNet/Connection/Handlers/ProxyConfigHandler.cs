using System.Net;
using BricksNet.Contracts.Options;

namespace BricksNet.Connection.Handlers;

/// <summary>
/// Builds the <see cref="IWebProxy"/> assigned to the primary <see cref="SocketsHttpHandler"/>.
/// <see cref="SocketsHttpHandler"/> supports SOCKS natively and selects the protocol from the
/// proxy URI scheme, so <see cref="ProxyOptions.Type"/> is applied by normalising that scheme.
/// </summary>
internal static class ProxyConfigHandler
{
    public static IWebProxy? CreateProxy(ProxyOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var proxy = new WebProxy(ResolveProxyUri(options))
        {
            Credentials = options.Credentials,
        };

        return proxy;
    }

    internal static Uri ResolveProxyUri(ProxyOptions options)
    {
        var uri = options.Uri;
        var isSocksScheme = uri.Scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase);

        switch (options.Type)
        {
            case ProxyType.Socks5 when !isSocksScheme:
                // e.g. "http://proxy:1080" with Type = Socks5 → "socks5://proxy:1080".
                return new UriBuilder(uri) { Scheme = "socks5" }.Uri;

            case ProxyType.Http when isSocksScheme:
                throw new ArgumentException(
                    $"Proxy URI scheme '{uri.Scheme}' conflicts with ProxyType.Http. "
                    + "Use an http:// URI or set ProxyType.Socks5.",
                    nameof(options));

            default:
                return uri;
        }
    }
}
