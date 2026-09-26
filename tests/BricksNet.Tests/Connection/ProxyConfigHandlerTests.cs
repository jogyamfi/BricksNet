using BricksNet.Connection.Handlers;
using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.Connection;

public sealed class ProxyConfigHandlerTests
{
    [Fact]
    public void Socks5Type_WithHttpScheme_IsRewrittenToSocks5()
    {
        var uri = ProxyConfigHandler.ResolveProxyUri(new ProxyOptions
        {
            Uri = new Uri("http://proxy.corp:1080"),
            Type = ProxyType.Socks5,
        });

        Assert.Equal("socks5://proxy.corp:1080/", uri.ToString());
    }

    [Theory]
    [InlineData("http://proxy.corp:8080/", ProxyType.Http)]
    [InlineData("socks5://proxy.corp:1080/", ProxyType.Socks5)]
    [InlineData("socks4a://proxy.corp:1080/", ProxyType.Socks5)]
    public void MatchingScheme_IsKept(string proxy, ProxyType type)
    {
        var uri = ProxyConfigHandler.ResolveProxyUri(new ProxyOptions { Uri = new Uri(proxy), Type = type });

        Assert.Equal(proxy, uri.ToString());
    }

    [Fact]
    public void HttpType_WithSocksScheme_Throws()
    {
        var options = new ProxyOptions { Uri = new Uri("socks5://proxy.corp:1080"), Type = ProxyType.Http };

        Assert.Throws<ArgumentException>(() => ProxyConfigHandler.ResolveProxyUri(options));
    }
}
