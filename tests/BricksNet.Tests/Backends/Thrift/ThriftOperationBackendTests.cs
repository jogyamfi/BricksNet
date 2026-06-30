using BricksNet.Backends.Thrift.Generated;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Results;
using Thrift;
using Thrift.Protocol;
using Xunit;

namespace BricksNet.Backends.Thrift;

/// <summary>
/// Unit tests for ThriftOperationBackend exercised through FakeThriftTransport.
/// Each test pre-loads the exact binary responses the transport will serve.
/// </summary>
public sealed class ThriftOperationBackendTests
{
    private static readonly Guid OpGuid = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    // ── factory helpers ───────────────────────────────────────────────────

    private static (ThriftOperationBackend Op, FakeThriftTransport Transport)
        Create(params byte[][] frames)
    {
        var transport = new FakeThriftTransport(frames);
        var protocol = new TBinaryProtocol(transport);
        var client = new TCLIServiceClient(protocol);
        var handle = MakeOperationHandle(OpGuid);
        return (new ThriftOperationBackend(client, handle), transport);
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
    public void Constructor_AssignsId_FromOperationHandle()
    {
        var (op, _) = Create();

        var expected = Convert.ToHexString(OpGuid.ToByteArray()).ToLowerInvariant();
        Assert.Equal(expected, op.Id);
    }

    [Fact]
    public void Constructor_NullClient_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new ThriftOperationBackend(null!, MakeOperationHandle(OpGuid)));

    [Fact]
    public void Constructor_NullHandle_Throws()
    {
        var transport = new FakeThriftTransport();
        var protocol = new TBinaryProtocol(transport);
        var client = new TCLIServiceClient(protocol);

        Assert.Throws<ArgumentNullException>(() => new ThriftOperationBackend(client, null!));
    }

    // ── GetStatusAsync ────────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(TOperationState.InitializedState), nameof(QueryStatus.Pending))]
    [InlineData(nameof(TOperationState.PendingState), nameof(QueryStatus.Pending))]
    [InlineData(nameof(TOperationState.RunningState), nameof(QueryStatus.Running))]
    [InlineData(nameof(TOperationState.FinishedState), nameof(QueryStatus.Succeeded))]
    [InlineData(nameof(TOperationState.CanceledState), nameof(QueryStatus.Cancelled))]
    [InlineData(nameof(TOperationState.ClosedState), nameof(QueryStatus.Closed))]
    [InlineData(nameof(TOperationState.ErrorState), nameof(QueryStatus.Failed))]
    [InlineData(nameof(TOperationState.TimedoutState), nameof(QueryStatus.Failed))]
    public async Task GetStatusAsync_MapsOperationState(string stateName, string expectedName)
    {
        var state = Enum.Parse<TOperationState>(stateName);
        var expected = Enum.Parse<QueryStatus>(expectedName);

        var frame = ThriftBinaryEncoder.GetOperationStatusFrame("GetOperationStatus", state);
        var (op, _) = Create(frame);

        var status = await op.GetStatusAsync();

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task GetStatusAsync_UnknownState_ReturnsPending()
    {
        var frame = ThriftBinaryEncoder.GetOperationStatusFrame("GetOperationStatus", TOperationState.UnknownState);
        var (op, _) = Create(frame);

        var status = await op.GetStatusAsync();

        Assert.Equal(QueryStatus.Pending, status);
    }

    // ── FetchResultsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task FetchResultsAsync_NullOptions_Throws()
    {
        var (op, _) = Create();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => op.FetchResultsAsync(null!));
    }

    [Fact]
    public async Task FetchResultsAsync_EmptyResult_ReturnsArrowFormat()
    {
        // FetchResultsAsync calls GetResultSetMetadata first (if not cached), then FetchResults.
        var metaFrame = ThriftBinaryEncoder.GetResultSetMetadataFrame("GetResultSetMetadata");
        var fetchFrame = ThriftBinaryEncoder.FetchResultsEmptyFrame("FetchResults");
        var (op, _) = Create(metaFrame, fetchFrame);

        var result = await op.FetchResultsAsync(new FetchOptions());

        Assert.Equal(ResultFormat.Arrow, result.Format);
        Assert.True(result.RawPayload.IsEmpty);
        Assert.Null(result.ExternalLinks);
    }

    [Fact]
    public async Task FetchResultsAsync_CachesMetadata_SecondCallDoesNotRefetch()
    {
        // Only one metadata frame queued — if FetchResultsAsync re-fetches metadata on the
        // second call it would consume the fetch frame as metadata and break.
        var metaFrame = ThriftBinaryEncoder.GetResultSetMetadataFrame("GetResultSetMetadata");
        var fetch1 = ThriftBinaryEncoder.FetchResultsEmptyFrame("FetchResults");
        var fetch2 = ThriftBinaryEncoder.FetchResultsEmptyFrame("FetchResults");
        var (op, _) = Create(metaFrame, fetch1, fetch2);

        var r1 = await op.FetchResultsAsync(new FetchOptions());
        var r2 = await op.FetchResultsAsync(new FetchOptions());

        Assert.Equal(ResultFormat.Arrow, r1.Format);
        Assert.Equal(ResultFormat.Arrow, r2.Format);
    }

    // ── CancelAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_SendsRpc_NoThrow()
    {
        var frame = ThriftBinaryEncoder.StatusOnlyFrame("CancelOperation");
        var (op, _) = Create(frame);

        await op.CancelAsync();
    }

    // ── CloseAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseAsync_SendsRpc_NoThrow()
    {
        var frame = ThriftBinaryEncoder.StatusOnlyFrame("CloseOperation");
        var (op, _) = Create(frame);

        await op.CloseAsync();
    }

    [Fact]
    public async Task CloseAsync_IsIdempotent()
    {
        // Only one frame queued; second call should be a no-op.
        var frame = ThriftBinaryEncoder.StatusOnlyFrame("CloseOperation");
        var (op, _) = Create(frame);

        await op.CloseAsync();
        await op.CloseAsync(); // must not send a second RPC
    }
}
