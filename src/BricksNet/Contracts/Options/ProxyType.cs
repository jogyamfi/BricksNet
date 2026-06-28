namespace BricksNet.Contracts.Options;

/// <summary>Transport protocol for the proxy connection.</summary>
public enum ProxyType
{
    /// <summary>HTTP/HTTPS proxy.</summary>
    Http,

    /// <summary>SOCKS5 proxy (supported natively by <c>SocketsHttpHandler</c> in .NET 8).</summary>
    Socks5,
}
