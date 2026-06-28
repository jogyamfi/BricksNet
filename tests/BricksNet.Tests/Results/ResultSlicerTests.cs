using Moq;
using Xunit;
using BricksNet.Contracts;

namespace BricksNet.Results;

public class ResultSlicerTests
{
    // --- helpers ---

    private static IResultsProvider ProviderOf(params IReadOnlyList<IReadOnlyDictionary<string, object?>>[] chunks)
    {
        var mock = new Mock<IResultsProvider>(MockBehavior.Strict);
        var queue = new Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(chunks);

        mock.SetupSequence(p => p.HasMoreRows)
            .Returns(true)
            .Returns(queue.Count > 1)
            .Returns(false);

        // Rebuild HasMoreRows from queue count to be dynamic.
        int callCount = 0;
        mock.Setup(p => p.HasMoreRows)
            .Returns(() => queue.Count > 0);
        mock.Setup(p => p.FetchNextChunkAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return queue.Count > 0 ? queue.Dequeue() : Array.Empty<IReadOnlyDictionary<string, object?>>();
            });

        return mock.Object;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(params int[] ids)
    {
        return ids.Select(id => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["id"] = id })
            .ToList();
    }

    // ---- unlimited (maxRows <= 0) ----

    [Fact]
    public async Task ZeroMaxRows_PassesAllRowsThrough()
    {
        var provider = ProviderOf(Rows(1, 2, 3));
        var slicer = new ResultSlicer(provider, maxRows: 0);

        Assert.True(slicer.HasMoreRows);
        var chunk = await slicer.FetchNextChunkAsync();
        Assert.Equal(3, chunk.Count);
    }

    // ---- cap mid-chunk ----

    [Fact]
    public async Task LimitLessThanChunk_ReturnsTruncatedChunk()
    {
        var provider = ProviderOf(Rows(1, 2, 3, 4, 5));
        var slicer = new ResultSlicer(provider, maxRows: 3);

        var chunk = await slicer.FetchNextChunkAsync();
        Assert.Equal(3, chunk.Count);
        Assert.Equal(1, chunk[0]["id"]);
        Assert.Equal(2, chunk[1]["id"]);
        Assert.Equal(3, chunk[2]["id"]);
        Assert.False(slicer.HasMoreRows);
    }

    // ---- cap at exact chunk boundary ----

    [Fact]
    public async Task LimitEqualsChunkSize_ReturnsFullChunk_AndHasMoreRowsFalse()
    {
        var provider = ProviderOf(Rows(10, 20));
        var slicer = new ResultSlicer(provider, maxRows: 2);

        var chunk = await slicer.FetchNextChunkAsync();
        Assert.Equal(2, chunk.Count);
        Assert.False(slicer.HasMoreRows);
    }

    // ---- cap spanning multiple chunks ----

    [Fact]
    public async Task LimitSpansChunks_CapsAtCorrectTotal()
    {
        var provider = ProviderOf(Rows(1, 2), Rows(3, 4, 5));
        var slicer = new ResultSlicer(provider, maxRows: 3);

        var chunk1 = await slicer.FetchNextChunkAsync();
        Assert.Equal(2, chunk1.Count);
        Assert.True(slicer.HasMoreRows);

        var chunk2 = await slicer.FetchNextChunkAsync();
        Assert.Single(chunk2);  // only 1 of the 3 rows in chunk2 is returned
        Assert.Equal(3, chunk2[0]["id"]);
        Assert.False(slicer.HasMoreRows);
    }

    // ---- further calls return empty once capped ----

    [Fact]
    public async Task AfterLimit_FetchNextChunkAsync_ReturnsEmpty()
    {
        var provider = ProviderOf(Rows(1));
        var slicer = new ResultSlicer(provider, maxRows: 1);

        await slicer.FetchNextChunkAsync();
        Assert.False(slicer.HasMoreRows);

        var extra = await slicer.FetchNextChunkAsync();
        Assert.Empty(extra);
    }

    // ---- null inner ----

    [Fact]
    public void Constructor_ThrowsOnNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new ResultSlicer(null!, maxRows: 10));
    }
}
