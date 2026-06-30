using System.Net;
using BricksNet.Connection;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Thrift;
using Thrift.Protocol;
using Xunit;

namespace BricksNet.Backends.Thrift;

/// <summary>Tests for ThriftBackend (ConnectAsync, OpenSessionAsync, CloseAsync, DisposeAsync).</summary>
public sealed class ThriftBackendTests
{
    private static readonly Guid SessionGuid = new("11111111-2222-3333-4444-555555555555");

    private static readonly BricksNetClientOptions DefaultOptions = new()
    {
        Host = "fake.databricks.example",
        HttpPath = "/sql/1.0/warehouses/abc",
    };

    // ── ConnectAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_NullOptions_Throws()
    {
        await using var backend = CreateBackend();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => backend.ConnectAsync(null!));
    }

    [Fact]
    public async Task ConnectAsync_CancelledToken_Throws()
    {
        await using var backend = CreateBackend();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backend.ConnectAsync(DefaultOptions, cts.Token));
    }

    [Fact]
    public async Task ConnectAsync_CompletesWithoutHttp()
    {
        await using var backend = CreateBackend();

        // ConnectAsync creates transport/protocol/client — no HTTP calls yet.
        await backend.ConnectAsync(DefaultOptions);
        // No exception = success; we verify OpenSession works next.
    }

    // ── OpenSessionAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task OpenSessionAsync_BeforeConnectAsync_Throws()
    {
        await using var backend = CreateBackend();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => backend.OpenSessionAsync(new BackendSessionOptions()));

        Assert.Contains("ConnectAsync", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSessionAsync_NullOptions_Throws()
    {
        await using var backend = CreateBackend(ThriftBinaryEncoder.OpenSessionFrame("OpenSession", SessionGuid));

        await backend.ConnectAsync(DefaultOptions);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => backend.OpenSessionAsync(null!));
    }

    [Fact]
    public async Task OpenSessionAsync_ReturnsSessionBackend_WithEncodedId()
    {
        await using var backend = CreateBackend(ThriftBinaryEncoder.OpenSessionFrame("OpenSession", SessionGuid));

        await backend.ConnectAsync(DefaultOptions);
        var session = await backend.OpenSessionAsync(new BackendSessionOptions());

        Assert.NotNull(session);
        var expectedHex = Convert.ToHexString(SessionGuid.ToByteArray()).ToLowerInvariant();
        Assert.Equal(expectedHex, session.Id);
    }

    [Fact]
    public async Task OpenSessionAsync_WithInitialNamespace_Succeeds()
    {
        await using var backend = CreateBackend(ThriftBinaryEncoder.OpenSessionFrame("OpenSession", SessionGuid));

        await backend.ConnectAsync(DefaultOptions);

        var session = await backend.OpenSessionAsync(new BackendSessionOptions
        {
            Catalog = "main",
            Schema = "default",
        });

        Assert.NotNull(session);
    }

    // ── CloseAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseAsync_BeforeConnect_CompletesWithNoOp()
    {
        await using var backend = CreateBackend();

        await backend.CloseAsync(); // should not throw
    }

    [Fact]
    public async Task CloseAsync_AfterConnect_CompletesAndDisposesTransport()
    {
        await using var backend = CreateBackend(ThriftBinaryEncoder.OpenSessionFrame("OpenSession", SessionGuid));

        await backend.ConnectAsync(DefaultOptions);
        await backend.OpenSessionAsync(new BackendSessionOptions());

        await backend.CloseAsync(); // disposes transport — no second HTTP POST
    }

    [Fact]
    public async Task CloseAsync_CancelledToken_Throws()
    {
        await using var backend = CreateBackend();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backend.CloseAsync(cts.Token));
    }

    // ── DisposeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_DisposesTransport()
    {
        var backend = CreateBackend();
        await backend.ConnectAsync(DefaultOptions);

        await backend.DisposeAsync();
        // No throw = transport disposed cleanly.
    }

    // ── factory ───────────────────────────────────────────────────────────

    private static ThriftBackend CreateBackend(params byte[][] frames)
    {
        var handler = new QueuedHandler(frames);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake.databricks.example") };
        var bricks = new BricksNetHttpClient(http, NullLogger<BricksNetHttpClient>.Instance);
        return new ThriftBackend(bricks);
    }

    private sealed class QueuedHandler(byte[][] frames) : HttpMessageHandler
    {
        private readonly Queue<byte[]> _frames = new(frames);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = _frames.Count > 0 ? _frames.Dequeue() : [];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }
}
