using System.Net;
using BricksNet.AdoNet;
using BricksNet.Connection;
using Microsoft.Extensions.Logging.Abstractions;
using Thrift;
using Xunit;

namespace BricksNet.Backends.Thrift;

public sealed class ThriftHttpTransportTests
{
    private const string Endpoint = "https://example.test/sql";

    private static ThriftHttpTransport CreateTransport(StubHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var bricks = new BricksNetHttpClient(httpClient, NullLogger<BricksNetHttpClient>.Instance);
        return new ThriftHttpTransport(bricks, Endpoint, new TConfiguration { MaxMessageSize = int.MaxValue });
    }

    [Fact]
    public async Task FlushAsync_PostsBufferedBytes_AndBuffersResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new byte[] { 9, 8, 7 });
        using var transport = CreateTransport(handler);

        Assert.True(transport.IsOpen);
        await transport.OpenAsync(CancellationToken.None);

        var payload = new byte[] { 1, 2, 3 };
        await transport.WriteAsync(payload, 0, payload.Length, CancellationToken.None);
        await transport.FlushAsync(CancellationToken.None);

        Assert.Equal(payload, handler.LastRequestBody);
        Assert.Equal("application/x-thrift", handler.LastContentType);

        var read = new byte[8];
        var count = await transport.ReadAsync(read, 0, read.Length, CancellationToken.None);
        Assert.Equal(3, count);
        Assert.Equal(new byte[] { 9, 8, 7 }, read[..3]);
    }

    [Fact]
    public async Task FlushAsync_NonSuccess_Throws_WithStatusCode()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "boom"u8.ToArray());
        using var transport = CreateTransport(handler);
        await transport.WriteAsync([0x1], 0, 1, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BricksNetException>(
            () => transport.FlushAsync(CancellationToken.None));

        Assert.Equal(500, ex.ErrorCode);
        Assert.Contains("boom", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_MarksTransportClosed()
    {
        var handler = new StubHandler(HttpStatusCode.OK, []);
        var transport = CreateTransport(handler);

        Assert.True(transport.IsOpen);
        transport.Close();

        Assert.False(transport.IsOpen);
    }

    [Fact]
    public async Task ReadAsync_CancelledToken_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.OK, []);
        using var transport = CreateTransport(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.ReadAsync(new byte[1], 0, 1, cts.Token).AsTask());
    }

    [Fact]
    public async Task WriteAsync_CancelledToken_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.OK, []);
        using var transport = CreateTransport(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.WriteAsync(new byte[1], 0, 1, cts.Token));
    }

    private sealed class StubHandler(HttpStatusCode status, byte[] responseBody) : HttpMessageHandler
    {
        public byte[]? LastRequestBody { get; private set; }

        public string? LastContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                LastContentType = request.Content.Headers.ContentType?.MediaType;
            }

            return new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(responseBody),
            };
        }
    }
}
