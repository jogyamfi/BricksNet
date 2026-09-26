using System.Net.Http.Headers;
using BricksNet.AdoNet;
using BricksNet.Connection;
using Thrift;
using Thrift.Transport;

namespace BricksNet.Backends.Thrift;

/// <summary>
/// A Thrift transport that tunnels the binary protocol over HTTP using
/// <see cref="BricksNetHttpClient"/>. Output is buffered until <see cref="FlushAsync"/>,
/// which POSTs the buffered frame to the Databricks SQL endpoint and buffers the full
/// response body for subsequent reads (avoiding <c>HttpContent</c> stream-position issues).
/// </summary>
internal sealed class ThriftHttpTransport : TEndpointTransport
{
    private const string ThriftContentType = "application/x-thrift";

    private readonly BricksNetHttpClient _httpClient;
    private readonly string _endpointUri;

    private MemoryStream _writeBuffer = new();
    private MemoryStream _readBuffer = new();
    private bool _disposed;

    public ThriftHttpTransport(BricksNetHttpClient httpClient, string endpointUri, TConfiguration configuration)
        : base(configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpointUri = endpointUri ?? throw new ArgumentNullException(nameof(endpointUri));
    }

    public override bool IsOpen => !_disposed;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override void Close()
    {
        _readBuffer.SetLength(0);
        _writeBuffer.SetLength(0);
        _disposed = true;
    }

    public override ValueTask<int> ReadAsync(byte[] buffer, int offset, int length, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = _readBuffer.Read(buffer, offset, length);
        CountConsumedMessageBytes(read);
        return new ValueTask<int>(read);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int length, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _writeBuffer.Write(buffer, offset, length);
        return Task.CompletedTask;
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        ResetMessageSizeAndConsumedBytes();

        var payload = _writeBuffer.ToArray();
        _writeBuffer.SetLength(0);

        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue(ThriftContentType);

        using var response = await _httpClient
            .PostAsync(_endpointUri, content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new BricksNetException(
                $"The Databricks Thrift endpoint returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {body}".Trim(),
                sqlState: null,
                errorCode: (int)response.StatusCode,
                queryId: null);
        }

        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        _readBuffer.Dispose();
        _readBuffer = new MemoryStream(responseBytes, writable: false);
        ResetMessageSizeAndConsumedBytes(responseBytes.LongLength);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _writeBuffer.Dispose();
            _readBuffer.Dispose();
        }

        _disposed = true;
    }
}
