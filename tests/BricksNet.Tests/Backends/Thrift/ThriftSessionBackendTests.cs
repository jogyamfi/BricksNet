using BricksNet.Backends.Thrift.Generated;
using BricksNet.Contracts.Options;
using Thrift;
using Thrift.Protocol;
using Xunit;

namespace BricksNet.Backends.Thrift;

/// <summary>
/// Unit tests for ThriftSessionBackend exercised through FakeThriftTransport.
/// Each test pre-loads the binary responses the transport will serve.
/// </summary>
public sealed class ThriftSessionBackendTests
{
    private static readonly Guid SessionGuid = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OpGuid = new("11111111-2222-3333-4444-555555555555");

    // ── factory helpers ───────────────────────────────────────────────────

    private static (ThriftSessionBackend Session, TCLIServiceClient Client)
        Create(params byte[][] frames)
    {
        var transport = new FakeThriftTransport(frames);
        var protocol = new TBinaryProtocol(transport);
        var client = new TCLIServiceClient(protocol);

        var handle = MakeSessionHandle(SessionGuid);
        var options = new BackendSessionOptions();
        var session = new ThriftSessionBackend(client, handle, options);
        return (session, client);
    }

    private static TSessionHandle MakeSessionHandle(Guid guid)
    {
        var guidBytes = guid.ToByteArray();
        return new TSessionHandle
        {
            SessionId = new THandleIdentifier { Guid = guidBytes, Secret = guidBytes },
        };
    }

    private static TOperationHandle MakeOperationHandle(Guid guid)
    {
        var guidBytes = guid.ToByteArray();
        return new TOperationHandle
        {
            OperationId = new THandleIdentifier { Guid = guidBytes, Secret = guidBytes },
            OperationType = TOperationType.ExecuteStatement,
            HasResultSet = true,
        };
    }

    // ── constructor ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_AssignsId_FromSessionHandle()
    {
        var (session, _) = Create();

        var expected = Convert.ToHexString(SessionGuid.ToByteArray()).ToLowerInvariant();
        Assert.Equal(expected, session.Id);
    }

    [Fact]
    public void Constructor_NullClient_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new ThriftSessionBackend(null!, MakeSessionHandle(SessionGuid), new BackendSessionOptions()));

    [Fact]
    public void Constructor_NullHandle_Throws()
    {
        var transport = new FakeThriftTransport();
        var protocol = new TBinaryProtocol(transport);
        var client = new TCLIServiceClient(protocol);

        Assert.Throws<ArgumentNullException>(() =>
            new ThriftSessionBackend(client, null!, new BackendSessionOptions()));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var transport = new FakeThriftTransport();
        var protocol = new TBinaryProtocol(transport);
        var client = new TCLIServiceClient(protocol);

        Assert.Throws<ArgumentNullException>(() =>
            new ThriftSessionBackend(client, MakeSessionHandle(SessionGuid), null!));
    }

    // ── ExecuteStatementAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteStatementAsync_NullSql_Throws()
    {
        var (session, _) = Create();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => session.ExecuteStatementAsync(null!, options: null));
    }

    [Fact]
    public async Task ExecuteStatementAsync_ReturnsOperationBackend_WithId()
    {
        var frame = ThriftBinaryEncoder.ExecuteStatementFrame("ExecuteStatement", OpGuid);
        var (session, _) = Create(frame);

        var op = await session.ExecuteStatementAsync("SELECT 1", options: null);

        var expectedHex = Convert.ToHexString(OpGuid.ToByteArray()).ToLowerInvariant();
        Assert.Equal(expectedHex, op.Id);
    }

    [Fact]
    public async Task ExecuteStatementAsync_WithQueryTimeout_Succeeds()
    {
        var frame = ThriftBinaryEncoder.ExecuteStatementFrame("ExecuteStatement", OpGuid);
        var (session, _) = Create(frame);

        var op = await session.ExecuteStatementAsync("SELECT 2",
            options: new StatementOptions { QueryTimeoutSeconds = 30 });

        Assert.NotNull(op);
    }

    [Fact]
    public async Task ExecuteStatementAsync_WithParameters_Succeeds()
    {
        var frame = ThriftBinaryEncoder.ExecuteStatementFrame("ExecuteStatement", OpGuid);
        var (session, _) = Create(frame);

        var op = await session.ExecuteStatementAsync("SELECT ?",
            options: new StatementOptions
            {
                Parameters = [
                    new BricksNetParameter("p1", 42),
                    new BricksNetParameter("p2", "hello"),
                    new BricksNetParameter("p3", true),
                    new BricksNetParameter("p4", null),
                    new BricksNetParameter("p5", 3.14),
                ],
            });

        Assert.NotNull(op);
    }

    // ── GetCatalogsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetCatalogsAsync_ReturnsOperationBackend()
    {
        var frame = ThriftBinaryEncoder.ExecuteStatementFrame("GetCatalogs", OpGuid);
        var (session, _) = Create(frame);

        var op = await session.GetCatalogsAsync();

        Assert.NotNull(op);
    }

    // ── GetSchemasAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetSchemasAsync_WithCatalogAndPattern_ReturnsOperation()
    {
        var frame = ThriftBinaryEncoder.ExecuteStatementFrame("GetSchemas", OpGuid);
        var (session, _) = Create(frame);

        var op = await session.GetSchemasAsync("main", "pub%");

        Assert.NotNull(op);
    }

    [Fact]
    public async Task GetSchemasAsync_NullArgs_Succeeds()
    {
        var frame = ThriftBinaryEncoder.ExecuteStatementFrame("GetSchemas", OpGuid);
        var (session, _) = Create(frame);

        var op = await session.GetSchemasAsync(null, null);

        Assert.NotNull(op);
    }

    // ── GetTablesAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetTablesAsync_ReturnsOperation()
    {
        var frame = ThriftBinaryEncoder.ExecuteStatementFrame("GetTables", OpGuid);
        var (session, _) = Create(frame);

        var op = await session.GetTablesAsync("cat", "sch%", "tbl%");

        Assert.NotNull(op);
    }

    // ── GetColumnsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetColumnsAsync_ReturnsOperation()
    {
        var frame = ThriftBinaryEncoder.ExecuteStatementFrame("GetColumns", OpGuid);
        var (session, _) = Create(frame);

        var op = await session.GetColumnsAsync("cat", "sch", "tbl", "col%");

        Assert.NotNull(op);
    }

    // ── CloseSessionAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CloseSessionAsync_SendsRpc()
    {
        var frame = ThriftBinaryEncoder.StatusOnlyFrame("CloseSession");
        var (session, _) = Create(frame);

        await session.CloseSessionAsync(); // no throw = success
    }

    [Fact]
    public async Task CloseSessionAsync_IsIdempotent()
    {
        var frame = ThriftBinaryEncoder.StatusOnlyFrame("CloseSession");
        var (session, _) = Create(frame);

        await session.CloseSessionAsync();
        await session.CloseSessionAsync(); // second call must not send another RPC
    }
}
