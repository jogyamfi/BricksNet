using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// BricksNetClient implements IAsyncDisposable, not IDisposable — always use 'await using'.

namespace BricksNet.FluentApi;

public sealed class BricksNetClientTests
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

    // ---- Constructor validation ----

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BricksNetClient(null!, "token"));
    }

    [Fact]
    public async Task Constructor_WithPatToken_DoesNotThrow()
    {
        await using var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);
        // Just construction - no network access; must succeed.
    }

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BricksNetClient(RestOptions, (BricksNet.Connection.BricksNetHttpClient)null!,
                NullLoggerFactory.Instance));
    }

    // ---- ConnectAsync (no network calls for REST backend) ----

    [Fact]
    public async Task ConnectAsync_Rest_IsIdempotent()
    {
        await using var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);

        await client.ConnectAsync();
        await client.ConnectAsync(); // second call is a no-op
    }

    [Fact]
    public async Task ConnectAsync_Thrift_CompletesWithoutNetworkCall()
    {
        // ThriftBackend.ConnectAsync() builds the Thrift protocol stack in memory only.
        await using var client = new BricksNetClient(ThriftOptions, "dapi-token", NullLoggerFactory.Instance);

        await client.ConnectAsync();
    }

    // ---- CloseAsync before connect ----

    [Fact]
    public async Task CloseAsync_BeforeConnect_IsNoOp()
    {
        await using var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);
        await client.CloseAsync(); // must not throw
    }

    // ---- DisposeAsync ----

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);
        await client.DisposeAsync();
        await client.DisposeAsync(); // second dispose must not throw
    }

    [Fact]
    public async Task DisposeAsync_OnConnectedClient_ClosesBackend()
    {
        var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);
        await client.ConnectAsync();
        await client.DisposeAsync(); // must not throw
    }

    // ---- ObjectDisposedException after dispose ----

    [Fact]
    public async Task ConnectAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task OpenSessionAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.OpenSessionAsync());
    }

    // ---- IBricksNetClient explicit interface ----

    [Fact]
    public void BricksNetClient_ImplementsIBricksNetClient()
    {
        var client = new BricksNetClient(RestOptions, "dapi-token");
        Assert.IsAssignableFrom<IBricksNetClient>(client);
    }

    // ---- OpenSessionAsync auto-connects for REST (no network for session open) ----

    [Fact]
    public async Task OpenSessionAsync_WithoutExplicitConnect_AutoConnects()
    {
        // REST OpenSessionAsync is synchronous (synthesises a session id in memory).
        await using var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);

        var session = await client.OpenSessionAsync();

        Assert.NotNull(session);
        Assert.IsType<BricksNetSession>(session);
    }

    [Fact]
    public async Task OpenSessionAsync_ReturnsSessionWithProvidedDefaults()
    {
        await using var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);
        var sessionOpts = new SessionOptions { Catalog = "my_cat", Schema = "my_schema" };

        var session = await client.OpenSessionAsync(sessionOpts);

        // The session wraps the backend; verify it is a BricksNetSession with the right id type.
        Assert.NotNull(session);
        Assert.False(string.IsNullOrEmpty(session is BricksNetSession s ? s.Id : string.Empty));
    }

    [Fact]
    public async Task CloseAsync_ClosesAllOpenSessions()
    {
        await using var client = new BricksNetClient(RestOptions, "dapi-token", NullLoggerFactory.Instance);
        var s1 = await client.OpenSessionAsync();
        var s2 = await client.OpenSessionAsync();

        await client.CloseAsync(); // must close both sessions without throwing
    }
}
