using System.Text;
using BricksNet.AdoNet;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BricksNet.FluentApi;

public sealed class BricksNetOperationTests
{
    // ---- helpers ----

    private static BricksNetOperation CreateOperation(
        IOperationBackend backend,
        int maxRows = 10_000)
        => new(backend, maxRows, "rest", NullLoggerFactory.Instance);

    /// <summary>
    /// Builds a FetchResult with inline JSON columnar data.
    /// The JSON format is an array of row-arrays (each cell is a JSON string).
    /// </summary>
    private static FetchResult JsonFetchResult(
        string json,
        params (string Name, string TypeName)[] columns)
    {
        var schema = columns
            .Select((c, i) => new ColumnInfo(c.Name, c.TypeName, i, true, null, null))
            .ToArray();
        return new FetchResult(
            ResultFormat.JsonColumnar,
            Encoding.UTF8.GetBytes(json),
            ExternalLinks: null,
            schema);
    }

    private static FetchResult EmptyArrowResult() =>
        new(ResultFormat.Arrow, ReadOnlyMemory<byte>.Empty, ExternalLinks: null,
            Schema: Array.Empty<ColumnInfo>());

    private static Mock<IOperationBackend> SucceededMock(FetchResult? fetch = null)
    {
        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("stmt-001");
        mock.Setup(m => m.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueryStatus.Succeeded);
        mock.Setup(m => m.FetchResultsAsync(It.IsAny<FetchOptions>()))
            .ReturnsAsync(fetch ?? EmptyArrowResult());
        return mock;
    }

    // ---- FetchAllAsync ----

    [Fact]
    public async Task FetchAllAsync_EmptyResult_ReturnsEmptyList()
    {
        var mock = SucceededMock(EmptyArrowResult());
        var operation = CreateOperation(mock.Object);

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Empty(rows);
    }

    [Fact]
    public async Task FetchAllAsync_JsonRows_ReturnsAllRows()
    {
        var json = "[[\"1\",\"Alice\"],[\"2\",\"Bob\"]]";
        var fetch = JsonFetchResult(json, ("id", "INT"), ("name", "STRING"));
        var mock = SucceededMock(fetch);
        var operation = CreateOperation(mock.Object);

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0]["id"]);
        Assert.Equal("Alice", rows[0]["name"]);
        Assert.Equal(2, rows[1]["id"]);
        Assert.Equal("Bob", rows[1]["name"]);
    }

    [Fact]
    public async Task FetchAllAsync_MapsToPoco_ViaJsonRoundtrip()
    {
        var json = "[[\"42\",\"Charlie\"]]";
        var fetch = JsonFetchResult(json, ("Id", "INT"), ("Name", "STRING"));
        var mock = SucceededMock(fetch);
        var operation = CreateOperation(mock.Object);

        var rows = await operation.FetchAllAsync<SampleRow>();

        Assert.Single(rows);
        Assert.Equal(42, rows[0].Id);
        Assert.Equal("Charlie", rows[0].Name);
    }

    [Fact]
    public async Task FetchAllAsync_Twice_ReturnsSameRows()
    {
        var json = "[[\"5\",\"Dave\"]]";
        var fetch = JsonFetchResult(json, ("id", "INT"), ("name", "STRING"));
        var mock = SucceededMock(fetch);
        var operation = CreateOperation(mock.Object);

        var first = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();
        var second = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Single(first);
        Assert.Empty(second); // provider exhausted after first call
    }

    // ---- FetchChunkAsync ----

    [Fact]
    public async Task FetchChunkAsync_ReturnsAtMostMaxRows()
    {
        var json = "[[\"1\",\"A\"],[\"2\",\"B\"],[\"3\",\"C\"]]";
        var fetch = JsonFetchResult(json, ("id", "INT"), ("name", "STRING"));
        var mock = SucceededMock(fetch);
        var operation = CreateOperation(mock.Object);

        var chunk1 = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(2);
        var chunk2 = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(2);
        var chunk3 = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(2);

        Assert.Equal(2, chunk1.Count);
        Assert.Equal(1, chunk1[0]["id"]);
        Assert.Single(chunk2);
        Assert.Equal(3, chunk2[0]["id"]);
        Assert.Empty(chunk3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FetchChunkAsync_InvalidMaxRows_Throws(int maxRows)
    {
        var mock = SucceededMock();
        var operation = CreateOperation(mock.Object);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(maxRows));
    }

    // ---- GetStatusAsync ----

    [Fact]
    public async Task GetStatusAsync_DelegatesToBackend()
    {
        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("s1");
        mock.Setup(m => m.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueryStatus.Running);
        var operation = CreateOperation(mock.Object);

        var status = await operation.GetStatusAsync();

        Assert.Equal(QueryStatus.Running, status);
        mock.Verify(m => m.GetStatusAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Status polling ----

    [Fact]
    public async Task FetchAllAsync_PollingRunningThenSucceeded_FetchesRows()
    {
        var callCount = 0;
        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("s2");
        mock.Setup(m => m.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount <= 2 ? QueryStatus.Running : QueryStatus.Succeeded);
        mock.Setup(m => m.FetchResultsAsync(It.IsAny<FetchOptions>()))
            .ReturnsAsync(EmptyArrowResult());
        var operation = CreateOperation(mock.Object);

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Empty(rows);
        Assert.Equal(3, callCount); // Running, Running, Succeeded
        mock.Verify(m => m.FetchResultsAsync(It.IsAny<FetchOptions>()), Times.Once);
    }

    [Fact]
    public async Task FetchAllAsync_FailedStatus_ThrowsBricksNetException()
    {
        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("s3");
        mock.Setup(m => m.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueryStatus.Failed);
        var operation = CreateOperation(mock.Object);

        var ex = await Assert.ThrowsAsync<BricksNetException>(() =>
            operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>());

        Assert.Equal("s3", ex.QueryId);
    }

    [Fact]
    public async Task FetchAllAsync_CancelledStatus_ThrowsBricksNetException()
    {
        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("s4");
        mock.Setup(m => m.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueryStatus.Cancelled);
        var operation = CreateOperation(mock.Object);

        await Assert.ThrowsAsync<BricksNetException>(() =>
            operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>());
    }

    // ---- Multi-page results ----

    private static Mock<IOperationBackend> PagedMock(params FetchResult[] pages)
    {
        var mock = SucceededMock();
        var index = 0;
        mock.Setup(m => m.FetchResultsAsync(It.IsAny<FetchOptions>()))
            .ReturnsAsync(() => pages[index++]);
        return mock;
    }

    [Fact]
    public async Task FetchAllAsync_FollowsBackendPages_UntilHasMoreRowsIsFalse()
    {
        var mock = PagedMock(
            JsonFetchResult("[[\"1\"],[\"2\"]]", ("id", "INT")) with { HasMoreRows = true },
            JsonFetchResult("[[\"3\"]]", ("id", "INT")) with { HasMoreRows = true },
            JsonFetchResult("[[\"4\"]]", ("id", "INT")));
        var operation = CreateOperation(mock.Object);

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Equal(new object?[] { 1, 2, 3, 4 }, rows.Select(r => r["id"]));
        mock.Verify(m => m.FetchResultsAsync(It.IsAny<FetchOptions>()), Times.Exactly(3));
    }

    [Fact]
    public async Task FetchAllAsync_SkipsEmptyIntermediatePage()
    {
        var mock = PagedMock(
            JsonFetchResult("[[\"1\"]]", ("id", "INT")) with { HasMoreRows = true },
            EmptyArrowResult() with { HasMoreRows = true },
            JsonFetchResult("[[\"2\"]]", ("id", "INT")));
        var operation = CreateOperation(mock.Object);

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task FetchChunkAsync_SpansBackendPages()
    {
        var mock = PagedMock(
            JsonFetchResult("[[\"1\"],[\"2\"]]", ("id", "INT")) with { HasMoreRows = true },
            JsonFetchResult("[[\"3\"],[\"4\"]]", ("id", "INT")));
        var operation = CreateOperation(mock.Object);

        var first = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(3);
        var second = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(3);
        var third = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(3);

        Assert.Equal(new object?[] { 1, 2, 3 }, first.Select(r => r["id"]));
        Assert.Equal(new object?[] { 4 }, second.Select(r => r["id"]));
        Assert.Empty(third);
    }

    [Fact]
    public async Task WaitForCompletionAsync_DoesNotFetchResults()
    {
        var mock = SucceededMock();
        var operation = CreateOperation(mock.Object);

        await operation.WaitForCompletionAsync();

        mock.Verify(m => m.FetchResultsAsync(It.IsAny<FetchOptions>()), Times.Never);
    }

    [Fact]
    public async Task WaitForCompletionAsync_FailedStatus_Throws()
    {
        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("s8");
        mock.Setup(m => m.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueryStatus.Failed);
        var operation = CreateOperation(mock.Object);

        await Assert.ThrowsAsync<BricksNetException>(() => operation.WaitForCompletionAsync());
    }

    [Fact]
    public async Task FetchAllAsync_CancellationToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("s5");
        mock.Setup(m => m.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var operation = CreateOperation(mock.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>(cts.Token));
    }

    // ---- CancelAsync ----

    [Fact]
    public async Task CancelAsync_DelegatesToBackend()
    {
        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("s6");
        mock.Setup(m => m.CancelAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var operation = CreateOperation(mock.Object);

        await operation.CancelAsync();

        mock.Verify(m => m.CancelAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_AfterClose_IsNoOp()
    {
        var mock = new Mock<IOperationBackend>();
        mock.Setup(m => m.Id).Returns("s7");
        mock.Setup(m => m.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var operation = CreateOperation(mock.Object);

        await operation.CloseAsync();
        await operation.CancelAsync(); // must not throw or call backend

        mock.Verify(m => m.CancelAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- CloseAsync / DisposeAsync ----

    [Fact]
    public async Task CloseAsync_DelegatesToBackend()
    {
        var mock = SucceededMock();
        mock.Setup(m => m.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var operation = CreateOperation(mock.Object);

        await operation.CloseAsync();

        mock.Verify(m => m.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloseAsync_IsIdempotent()
    {
        var mock = SucceededMock();
        mock.Setup(m => m.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var operation = CreateOperation(mock.Object);

        await operation.CloseAsync();
        await operation.CloseAsync();

        mock.Verify(m => m.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ClosesOperation()
    {
        var mock = SucceededMock();
        mock.Setup(m => m.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var operation = CreateOperation(mock.Object);

        await operation.DisposeAsync();

        mock.Verify(m => m.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- ObjectDisposedException after close ----

    [Fact]
    public async Task FetchAllAsync_AfterClose_ThrowsObjectDisposed()
    {
        var mock = SucceededMock();
        mock.Setup(m => m.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var operation = CreateOperation(mock.Object);
        await operation.CloseAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>());
    }

    [Fact]
    public async Task GetStatusAsync_AfterClose_ThrowsObjectDisposed()
    {
        var mock = SucceededMock();
        mock.Setup(m => m.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var operation = CreateOperation(mock.Object);
        await operation.CloseAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            operation.GetStatusAsync());
    }

    // ---- helper types ----

    private sealed class SampleRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
