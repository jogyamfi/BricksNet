using System.Net;
using BricksNet.Contracts.Options;

namespace BricksNet.Connection.Handlers;

/// <summary>
/// Builds an <see cref="IWebProxy"/> from <see cref="ProxyOptions"/> for use on the primary
/// <see cref="SocketsHttpHandler"/>.
/// </summary>
/// <remarks>
/// Proxy selection is a connection-level concern, so it is configured on the primary handler at
/// registration time rather than per-request. .NET 8's <see cref="SocketsHttpHandler"/> understands
/// both <c>http://</c> and <c>socks5://</c> proxy URIs natively, so no third-party package is needed.
/// </remarks>
internal static class ProxyConfigHandler
{
    /// <summary>
    /// Creates a <see cref="WebProxy"/> for the supplied options, or <see langword="null"/> when no
    /// proxy is configured.
    /// </summary>
    public static IWebProxy? CreateProxy(ProxyOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        // SocketsHttpHandler routes both HTTP and SOCKS5 by inspecting the proxy URI scheme,
        // so the same WebProxy construction path serves both ProxyType values.
        var proxy = new WebProxy(options.Uri)
        {
            Credentials = options.Credentials,
        };

        return proxy;
    }
}
