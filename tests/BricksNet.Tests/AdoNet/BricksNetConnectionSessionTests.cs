using System.Data;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.AdoNet;

/// <summary>
/// Instance-level client factories, opt-in session reuse (<c>KeepSessionAlive</c>) and server
/// version discovery on <see cref="BricksNetConnection"/>.
/// </summary>
public sealed class BricksNetConnectionSessionTests
{
    private const string BaseConnectionString = "Host=h;HttpPath=/sql/1.0/warehouses/x;Token=tok";

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> VersionRows =
        FakeFluent.Rows(FakeFluent.Row(("version()", "3.5.2 0a1b2c3d")));

    /// <summary>Counts clients and sessions created, and tracks the sessions still open.</summary>
    private sealed class CountingFactory(IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows = null)
    {
        private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows = rows ?? VersionRows;

        public int ClientsCreated { get; private set; }

        public List<FakeFluent.FakeSession> Sessions { get; } = [];

        public List<FakeFluent.FakeClient> Clients { get; } = [];

        public IBricksNetClient Create(BricksNetClientOptions options, string? accessToken)
        {
            ClientsCreated++;
            var client = new FakeFluent.FakeClient(_rows);
            Clients.Add(client);
            return new RecordingClient(client, Sessions);
        }

        private sealed class RecordingClient(FakeFluent.FakeClient inner, List<FakeFluent.FakeSession> sessions)
            : IBricksNetClient
        {
            public Task ConnectAsync(CancellationToken cancellationToken = default) => inner.ConnectAsync(cancellationToken);

            public async Task<IBricksNetSession> OpenSessionAsync(
                SessionOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                var session = (FakeFluent.FakeSession)await inner.OpenSessionAsync(options, cancellationToken);
                sessions.Add(session);
                return session;
            }

            public Task CloseAsync(CancellationToken cancellationToken = default) => inner.CloseAsync(cancellationToken);

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    [Fact]
    public void InstanceFactory_IsUsed_InsteadOfStaticFactory()
    {
        var factory = new CountingFactory();
        using var connection = new BricksNetConnection(BaseConnectionString, factory.Create);

        connection.Open();

        Assert.Equal(1, factory.ClientsCreated);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public void OptionsConstructor_AcceptsInstanceFactory()
    {
        var factory = new CountingFactory();
        var options = new BricksNetClientOptions { Host = "h", HttpPath = "/sql/1.0/warehouses/x" };
        using var connection = new BricksNetConnection(options, "tok", clientFactory: factory.Create);

        connection.Open();

        Assert.Equal(1, factory.ClientsCreated);
    }

    [Fact]
    public void KeepSessionAlive_IsReadFromConnectionString()
    {
        using var on = new BricksNetConnection(BaseConnectionString + ";KeepSessionAlive=true", (_, _) => null!);
        using var off = new BricksNetConnection(BaseConnectionString, (_, _) => null!);

        Assert.True(on.KeepSessionAlive);
        Assert.False(off.KeepSessionAlive);
    }

    [Fact]
    public async Task WithoutKeepSessionAlive_EachOpenStartsANewSession()
    {
        var factory = new CountingFactory();
        await using var connection = new BricksNetConnection(BaseConnectionString, factory.Create);

        await connection.OpenAsync();
        await connection.CloseAsync();
        await connection.OpenAsync();

        Assert.Equal(2, factory.ClientsCreated);
        Assert.True(factory.Sessions[0].Closed);
        Assert.True(factory.Clients[0].Closed);
    }

    [Fact]
    public async Task KeepSessionAlive_ReusesSessionAcrossCloseAndOpen()
    {
        var factory = new CountingFactory();
        var connection = new BricksNetConnection(BaseConnectionString + ";KeepSessionAlive=true", factory.Create);

        await connection.OpenAsync();
        await connection.CloseAsync();
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.False(factory.Sessions[0].Closed);

        await connection.OpenAsync();
        Assert.Equal(1, factory.ClientsCreated);
        Assert.Single(factory.Sessions);

        await connection.DisposeAsync();
        Assert.True(factory.Sessions[0].Closed);
        Assert.True(factory.Clients[0].Closed);
    }

    [Fact]
    public void KeepSessionAlive_SyncDispose_ReleasesKeptSession()
    {
        var factory = new CountingFactory();
        var connection = new BricksNetConnection(BaseConnectionString, factory.Create) { KeepSessionAlive = true };
        connection.Open();
        connection.Close();

        connection.Dispose();

        Assert.True(factory.Sessions[0].Closed);
        Assert.True(factory.Clients[0].Closed);
    }

    [Fact]
    public void KeepSessionAlive_ChangeDatabaseWhileClosed_ReleasesKeptSession()
    {
        var factory = new CountingFactory();
        using var connection = new BricksNetConnection(BaseConnectionString, factory.Create) { KeepSessionAlive = true };
        connection.Open();
        connection.Close();

        connection.ChangeDatabase("other");
        connection.Open();

        Assert.True(factory.Sessions[0].Closed);
        Assert.Equal(2, factory.Sessions.Count);
        Assert.Equal(1, factory.ClientsCreated);
        Assert.Equal("other", connection.Database);
    }

    [Fact]
    public void KeepSessionAlive_ChangingConnectionString_ReleasesKeptSession()
    {
        var factory = new CountingFactory();
        using var connection = new BricksNetConnection(BaseConnectionString, factory.Create) { KeepSessionAlive = true };
        connection.Open();
        connection.Close();

        connection.ConnectionString = BaseConnectionString + ";Schema=s2";

        Assert.True(factory.Sessions[0].Closed);
        Assert.True(factory.Clients[0].Closed);
    }

    [Fact]
    public async Task OpenFailure_ClosesTheCreatedClient()
    {
        var client = new ThrowingSessionClient();
        await using var connection = new BricksNetConnection(BaseConnectionString, (_, _) => client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync());

        Assert.True(client.Closed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task GetServerVersionAsync_ReturnsSparkVersion_AndCaches()
    {
        var factory = new CountingFactory();
        await using var connection = new BricksNetConnection(BaseConnectionString, factory.Create);
        await connection.OpenAsync();
        var session = factory.Sessions[0];

        Assert.Equal("3.5.2", await connection.GetServerVersionAsync());
        Assert.Equal("SELECT version()", session.LastSql);
        Assert.True(session.LastOperation!.Closed);

        var first = session.LastOperation;
        Assert.Equal("3.5.2", connection.ServerVersion);
        Assert.Same(first, session.LastOperation);
        Assert.Equal(new Version(3, 5, 2), connection.SparkVersion);
    }

    [Fact]
    public void ServerVersion_WhenClosedAndUnknown_IsEmpty()
    {
        using var connection = new BricksNetConnection(BaseConnectionString, new CountingFactory().Create);

        Assert.Equal(string.Empty, connection.ServerVersion);
        Assert.Null(connection.SparkVersion);
    }

    [Fact]
    public async Task ServerVersion_IsKept_WithKeptSession_AndClearedOnDispose()
    {
        var connection = new BricksNetConnection(BaseConnectionString, new CountingFactory().Create) { KeepSessionAlive = true };
        await connection.OpenAsync();
        await connection.GetServerVersionAsync();
        await connection.CloseAsync();

        Assert.Equal("3.5.2", connection.ServerVersion);

        await connection.DisposeAsync();
        Assert.Equal(string.Empty, connection.ServerVersion);
    }

    private sealed class ThrowingSessionClient : IBricksNetClient
    {
        public bool Closed { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IBricksNetSession> OpenSessionAsync(
            SessionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("session refused");

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Closed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
