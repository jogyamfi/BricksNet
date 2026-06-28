using Apache.Arrow;
using Apache.Arrow.Ipc;
using BricksNet.Contracts;

namespace BricksNet.Results;

/// <summary>
/// Streams rows from an Apache Arrow IPC payload. Each call to
/// <see cref="FetchNextChunkAsync"/> reads and converts a single <see cref="RecordBatch"/>,
/// keeping memory bounded to one batch at a time.
/// </summary>
public sealed class ArrowResultHandler : IResultsProvider, IDisposable
{
    private readonly Stream? _stream;
    private readonly ArrowStreamReader? _reader;
    private bool _hasMoreRows;

    /// <summary>Creates a handler over a buffered Arrow IPC payload.</summary>
    public ArrowResultHandler(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            _hasMoreRows = false;
            return;
        }

        _stream = new MemoryStream(payload.ToArray(), writable: false);
        _reader = new ArrowStreamReader(_stream);
        _hasMoreRows = true;
    }

    /// <summary>Creates a handler that reads Arrow IPC data from <paramref name="stream"/>.</summary>
    public ArrowResultHandler(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _reader = new ArrowStreamReader(stream);
        _hasMoreRows = true;
    }

    /// <inheritdoc />
    public bool HasMoreRows => _hasMoreRows;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FetchNextChunkAsync(
        CancellationToken cancellationToken = default)
    {
        if (_reader is null || !_hasMoreRows)
        {
            _hasMoreRows = false;
            return System.Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        using var batch = await _reader.ReadNextRecordBatchAsync(cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            _hasMoreRows = false;
            return System.Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        return ArrowTypeConverter.ConvertBatch(batch);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _reader?.Dispose();
        _stream?.Dispose();
    }
}
