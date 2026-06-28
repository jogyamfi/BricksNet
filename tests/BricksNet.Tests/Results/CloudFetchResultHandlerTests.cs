using System.Net;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using RichardSzalay.MockHttp;
using Xunit;

namespace BricksNet.Results;

public class CloudFetchResultHandlerTests
{
    // ---- helpers ----

    private static async Task<byte[]> BuildArrowIpcBytesAsync(string columnName, params int[] values)
    {
        var schema = new Schema(
            new List<Field> { new Field(columnName, Int32Type.Default, nullable: false) },
            metadata: null);
        var array = values.Aggregate(new Int32Array.Builder(), (b, v) => b.Append(v)).Build();
        using var batch = new RecordBatch(schema, new IArrowArray[] { array }, values.Length);

        using var ms = new MemoryStream();
        using (var writer = new ArrowStreamWriter(ms, schema, leaveOpen: true))
        {
            await writer.WriteRecordBatchAsync(batch);
            await writer.WriteEndAsync();
        }

        return ms.ToArray();
    }

    private static ExternalLink LinkAt(int chunkIndex, string url, DateTimeOffset expiry) =>
        new ExternalLink(
            Uri: new Uri(url),
            FileSizeBytes: 100,
            Expiration: expiry,
            ChunkIndex: chunkIndex,
            RowOffset: 0,
            RowCount: 1,
            HttpHeaders: null);

    private static readonly DateTimeOffset FarFuture = DateTimeOffset.UtcNow.AddHours(1);
    private static readonly DateTimeOffset AlreadyExpired = DateTimeOffset.UtcNow.AddSeconds(-5);

    // ---- no links ----

    [Fact]
    public void NoLinks_HasMoreRowsFalse()
    {
        using var client = new HttpClient();
        using var handler = new CloudFetchResultHandler(System.Array.Empty<ExternalLink>(), client);
        Assert.False(handler.HasMoreRows);
    }

    [Fact]
    public async Task NoLinks_FetchNextChunkAsync_ReturnsEmpty()
    {
        using var client = new HttpClient();
        using var handler = new CloudFetchResultHandler(System.Array.Empty<ExternalLink>(), client);
        var rows = await handler.FetchNextChunkAsync();
        Assert.Empty(rows);
    }

    // ---- expired link ----

    [Fact]
    public async Task ExpiredLink_ThrowsCloudFetchLinkExpiredException()
    {
        using var client = new HttpClient();
        var links = new[] { LinkAt(0, "https://example.com/chunk0", AlreadyExpired) };
        using var handler = new CloudFetchResultHandler(links, client);

        await Assert.ThrowsAsync<CloudFetchLinkExpiredException>(
            () => handler.FetchNextChunkAsync());
    }

    // ---- out-of-order links are sorted by chunk_index ----

    [Fact]
    public async Task LinksOutOfOrder_RowsReturnedInChunkIndexOrder()
    {
        var bytes0 = await BuildArrowIpcBytesAsync("n", 10);
        var bytes1 = await BuildArrowIpcBytesAsync("n", 20);

        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "https://s3.example.com/chunk0")
            .Respond(HttpStatusCode.OK, new ByteArrayContent(bytes0));
        mockHttp.When(HttpMethod.Get, "https://s3.example.com/chunk1")
            .Respond(HttpStatusCode.OK, new ByteArrayContent(bytes1));

        // Provide in REVERSE order — chunk1 first, then chunk0.
        var links = new[]
        {
            LinkAt(chunkIndex: 1, "https://s3.example.com/chunk1", FarFuture),
            LinkAt(chunkIndex: 0, "https://s3.example.com/chunk0", FarFuture),
        };

        using var handler = new CloudFetchResultHandler(links, mockHttp.ToHttpClient());

        Assert.True(handler.HasMoreRows);
        var rows0 = await handler.FetchNextChunkAsync();
        Assert.Single(rows0);
        Assert.Equal(10, rows0[0]["n"]);   // chunk 0

        var rows1 = await handler.FetchNextChunkAsync();
        Assert.Single(rows1);
        Assert.Equal(20, rows1[0]["n"]);   // chunk 1

        Assert.False(handler.HasMoreRows);
    }

    // ---- happy-path single link ----

    [Fact]
    public async Task SingleLink_DownloadsAndParsesArrowIpc()
    {
        var arrowBytes = await BuildArrowIpcBytesAsync("val", 42, 99);

        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "https://s3.example.com/chunk0")
            .Respond(HttpStatusCode.OK, new ByteArrayContent(arrowBytes));

        var links = new[] { LinkAt(0, "https://s3.example.com/chunk0", FarFuture) };
        using var handler = new CloudFetchResultHandler(links, mockHttp.ToHttpClient());

        var rows = await handler.FetchNextChunkAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(42, rows[0]["val"]);
        Assert.Equal(99, rows[1]["val"]);
    }

    // ---- extra headers are forwarded ----

    [Fact]
    public async Task Link_WithHttpHeaders_ForwardsHeadersInRequest()
    {
        var arrowBytes = await BuildArrowIpcBytesAsync("x", 1);
        string? capturedHeader = null;

        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "https://s3.example.com/h")
            .Respond(req =>
            {
                req.Headers.TryGetValues("x-amz-security-token", out var vals);
                capturedHeader = vals?.FirstOrDefault();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(arrowBytes),
                };
            });

        var links = new[]
        {
            new ExternalLink(
                Uri: new Uri("https://s3.example.com/h"),
                FileSizeBytes: 1,
                Expiration: FarFuture,
                ChunkIndex: 0,
                RowOffset: 0,
                RowCount: 1,
                HttpHeaders: new Dictionary<string, string>
                {
                    ["x-amz-security-token"] = "tok123",
                }),
        };

        using var handler = new CloudFetchResultHandler(links, mockHttp.ToHttpClient());
        await handler.FetchNextChunkAsync();

        Assert.Equal("tok123", capturedHeader);
    }

    // ---- cancellation ----

    [Fact]
    public async Task Cancellation_BeforeFetch_ThrowsOperationCanceled()
    {
        using var client = new HttpClient();
        var links = new[] { LinkAt(0, "https://s3.example.com/c", FarFuture) };
        using var handler = new CloudFetchResultHandler(links, client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.FetchNextChunkAsync(cts.Token));
    }
}
