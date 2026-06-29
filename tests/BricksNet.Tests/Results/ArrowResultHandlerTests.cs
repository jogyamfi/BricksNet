using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Xunit;

namespace BricksNet.Results;

public class ArrowResultHandlerTests
{
    // ---- helpers ----

    private static async Task<ReadOnlyMemory<byte>> BuildArrowIpcAsync(
        params (string Name, int[] Values)[] columns)
    {
        var fields = columns.Select(c => new Field(c.Name, Int32Type.Default, nullable: true)).ToList();
        var schema = new Schema(fields, metadata: null);

        IArrowArray[] arrays = columns
            .Select(c =>
            {
                var b = new Int32Array.Builder();
                foreach (var v in c.Values)
                {
                    b.Append(v);
                }

                return (IArrowArray)b.Build();
            })
            .ToArray();

        using var batch = new RecordBatch(schema, arrays, columns[0].Values.Length);

        using var ms = new MemoryStream();
        using (var writer = new ArrowStreamWriter(ms, schema, leaveOpen: true))
        {
            await writer.WriteRecordBatchAsync(batch);
            await writer.WriteEndAsync();
        }

        return ms.ToArray();
    }

    private static async Task<ReadOnlyMemory<byte>> BuildMultiBatchIpcAsync()
    {
        // Two record batches with the same schema.
        var schema = new Schema(
            new List<Field> { new Field("n", Int32Type.Default, nullable: false) },
            metadata: null);

        using var ms = new MemoryStream();
        using var writer = new ArrowStreamWriter(ms, schema, leaveOpen: true);

        using (var batch1 = new RecordBatch(
            schema, new IArrowArray[] { new Int32Array.Builder().Append(10).Build() }, 1))
        {
            await writer.WriteRecordBatchAsync(batch1);
        }

        using (var batch2 = new RecordBatch(
            schema, new IArrowArray[] { new Int32Array.Builder().Append(20).Append(30).Build() }, 2))
        {
            await writer.WriteRecordBatchAsync(batch2);
        }

        await writer.WriteEndAsync();
        return ms.ToArray();
    }

    // ---- empty payload ----

    [Fact]
    public void EmptyPayload_HasMoreRowsFalse_ReturnEmpty()
    {
        using var handler = new ArrowResultHandler(ReadOnlyMemory<byte>.Empty);
        Assert.False(handler.HasMoreRows);
    }

    [Fact]
    public async Task EmptyPayload_FetchNextChunk_ReturnsEmpty()
    {
        using var handler = new ArrowResultHandler(ReadOnlyMemory<byte>.Empty);
        var rows = await handler.FetchNextChunkAsync();
        Assert.Empty(rows);
    }

    // ---- single batch ----

    [Fact]
    public async Task SingleBatch_ReturnsRows_ThenHasMoreRowsFalse()
    {
        var payload = await BuildArrowIpcAsync(("x", new[] { 1, 2, 3 }));
        using var handler = new ArrowResultHandler(payload);

        Assert.True(handler.HasMoreRows);
        var rows = await handler.FetchNextChunkAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(1, rows[0]["x"]);
        Assert.Equal(2, rows[1]["x"]);
        Assert.Equal(3, rows[2]["x"]);

        // After the batch is exhausted the handler signals no more rows.
        var empty = await handler.FetchNextChunkAsync();
        Assert.Empty(empty);
        Assert.False(handler.HasMoreRows);
    }

    // ---- multiple batches ----

    [Fact]
    public async Task MultiBatch_ReturnedOneBatchPerCall()
    {
        var payload = await BuildMultiBatchIpcAsync();
        using var handler = new ArrowResultHandler(payload);

        // First call → batch1 (1 row)
        Assert.True(handler.HasMoreRows);
        var chunk1 = await handler.FetchNextChunkAsync();
        Assert.Single(chunk1);
        Assert.Equal(10, chunk1[0]["n"]);

        // Second call → batch2 (2 rows)
        var chunk2 = await handler.FetchNextChunkAsync();
        Assert.Equal(2, chunk2.Count);
        Assert.Equal(20, chunk2[0]["n"]);
        Assert.Equal(30, chunk2[1]["n"]);

        // Third call → end of stream
        var chunk3 = await handler.FetchNextChunkAsync();
        Assert.Empty(chunk3);
        Assert.False(handler.HasMoreRows);
    }

    // ---- CancellationToken ----

    [Fact]
    public async Task FetchNextChunkAsync_Cancellation_ThrowsOrCancels()
    {
        var payload = await BuildArrowIpcAsync(("c", new[] { 1 }));
        using var handler = new ArrowResultHandler(payload);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.FetchNextChunkAsync(cts.Token));
    }
}
