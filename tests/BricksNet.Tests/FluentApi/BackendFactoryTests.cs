using BricksNet.Backends;
using BricksNet.Backends.Rest;
using BricksNet.Backends.Thrift;
using BricksNet.Connection;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BricksNet.FluentApi;

public sealed class BackendFactoryTests
{
    private static BricksNetClientOptions RestOptions => new()
    {
        Host = "workspace.azuredatabricks.net",
        HttpPath = "/sql/1.0/warehouses/abc123",
        UseThrift = false,
    };

    private static BricksNetClientOptions ThriftOptions => new()
    {
        Host = "workspace.azuredatabricks.net",
        HttpPath = "/sql/protocolv1/o/0/abc123",
        UseThrift = true,
    };

    private static BricksNetHttpClient CreateHttpClient() =>
        new(new HttpClient { BaseAddress = new Uri("https://workspace.azuredatabricks.net") },
            NullLogger<BricksNetHttpClient>.Instance);

    // ---- Create ----

    [Fact]
    public void Create_UseThriftFalse_ReturnsRestBackend()
    {
        var backend = BackendFactory.Create(RestOptions, CreateHttpClient());
        Assert.IsType<RestBackend>(backend);
    }

    [Fact]
    public void Create_UseThriftTrue_ReturnsThriftBackend()
    {
        var backend = BackendFactory.Create(ThriftOptions, CreateHttpClient());
        Assert.IsType<ThriftBackend>(backend);
    }

    [Fact]
    public void Create_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BackendFactory.Create(null!, CreateHttpClient()));
    }

    [Fact]
    public void Create_NullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BackendFactory.Create(RestOptions, null!));
    }

    // ---- BackendName ----

    [Fact]
    public void BackendName_UseThriftFalse_ReturnsRest()
    {
        Assert.Equal("rest", BackendFactory.BackendName(RestOptions));
    }

    [Fact]
    public void BackendName_UseThriftTrue_ReturnsThrift()
    {
        Assert.Equal("thrift", BackendFactory.BackendName(ThriftOptions));
    }

    [Fact]
    public void BackendName_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BackendFactory.BackendName(null!));
    }
}
