using System.Diagnostics;
using System.Text.Json;
using BricksNet.AdoNet;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Results;
using BricksNet.Telemetry;
using Microsoft.Extensions.Logging;

namespace BricksNet;

/// <summary>
/// Fluent handle to a submitted statement. Waits for the operation to reach a terminal state on
/// first fetch, then pages rows lazily from the backend through an <see cref="IResultsProvider"/>.
/// Mirrors <c>DBSQLOperation</c> from <c>databricks-sql-nodejs/lib/DBSQLOperation.ts</c>.
/// </summary>
public sealed class BricksNetOperation : IBricksNetOperation
{
    private static readonly JsonSerializerOptions RowSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IOperationBackend _backend;
    private readonly int _maxRows;
    private readonly string _backendName;
    private readonly ILogger<BricksNetOperation> _logger;
    private readonly Queue<IReadOnlyDictionary<string, object?>> _buffer = new();

    private IResultsProvider? _provider;
    private bool _completed;
    private bool _closed;

    internal BricksNetOperation(
        IOperationBackend backend,
        int maxRows,
        string backendName,
        ILoggerFactory loggerFactory)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _maxRows = maxRows > 0 ? maxRows : 10_000;
        _backendName = backendName ?? throw new ArgumentNullException(nameof(backendName));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<BricksNetOperation>();
    }

    /// <summary>The server-assigned statement identifier.</summary>
    public string Id => _backend.Id;

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> FetchAllAsync<T>(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        var rows = await MaterializeAllAsync(cancellationToken).ConfigureAwait(false);
        return MapRows<T>(rows);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> FetchChunkAsync<T>(int maxRows, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        if (maxRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows), maxRows, "maxRows must be positive.");
        }

        await EnsureResultsAsync(cancellationToken).ConfigureAwait(false);
        await FillBufferAsync(maxRows, cancellationToken).ConfigureAwait(false);

        if (_buffer.Count == 0)
        {
            return Array.Empty<T>();
        }

        var take = Math.Min(maxRows, _buffer.Count);
        var taken = new List<IReadOnlyDictionary<string, object?>>(take);
        for (var i = 0; i < take; i++)
        {
            taken.Add(_buffer.Dequeue());
        }

        return MapRows<T>(taken);
    }

    /// <inheritdoc />
    public Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        return _backend.GetStatusAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return;
        }

        await _backend.CancelAsync(cancellationToken).ConfigureAwait(false);
        LogMessages.StatementCancelled(_logger, Id);
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        await _backend.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }

    private async Task<List<IReadOnlyDictionary<string, object?>>> MaterializeAllAsync(
        CancellationToken cancellationToken)
    {
        await EnsureResultsAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (_buffer.Count > 0)
        {
            rows.Add(_buffer.Dequeue());
        }

        while (_provider!.HasMoreRows)
        {
            var chunk = await _provider.FetchNextChunkAsync(cancellationToken).ConfigureAwait(false);
            if (chunk.Count == 0)
            {
                break;
            }

            rows.AddRange(chunk);
        }

        return rows;
    }

    /// <summary>
    /// Ensures the statement has completed and a results provider exists. A single backend fetch is
    /// issued; the provider then pages internally (Arrow batches, CloudFetch links). This covers the
    /// REST inline/CloudFetch results and the Thrift inline-Arrow/CloudFetch results in full.
    /// </summary>
    private async Task EnsureResultsAsync(CancellationToken cancellationToken)
    {
        if (_provider is not null)
        {
            return;
        }

        await EnsureCompletedAsync(cancellationToken).ConfigureAwait(false);

        using Activity? activity = BricksNetActivitySource.StartResultFetch(Id, _backendName);

        var fetch = await _backend
            .FetchResultsAsync(new FetchOptions { MaxRows = _maxRows, CancellationToken = cancellationToken })
            .ConfigureAwait(false);

        _provider = ResultHandlerFactory.Create(fetch);
    }

    private async Task FillBufferAsync(int target, CancellationToken cancellationToken)
    {
        while (_buffer.Count < target && _provider!.HasMoreRows)
        {
            var chunk = await _provider.FetchNextChunkAsync(cancellationToken).ConfigureAwait(false);
            if (chunk.Count == 0)
            {
                break;
            }

            foreach (var row in chunk)
            {
                _buffer.Enqueue(row);
            }
        }
    }

    private async Task EnsureCompletedAsync(CancellationToken cancellationToken)
    {
        if (_completed)
        {
            return;
        }

        var delayMs = 200;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await _backend.GetStatusAsync(cancellationToken).ConfigureAwait(false);

            switch (status)
            {
                case QueryStatus.Succeeded:
                case QueryStatus.Closed:
                    _completed = true;
                    LogMessages.StatementSucceeded(_logger, Id);
                    return;

                case QueryStatus.Failed:
                    LogMessages.StatementFailed(_logger, Id, errorCode: "0", sqlState: string.Empty);
                    throw new BricksNetException(
                        $"Statement '{Id}' failed.", sqlState: null, errorCode: 0, queryId: Id);

                case QueryStatus.Cancelled:
                    throw new BricksNetException(
                        $"Statement '{Id}' was cancelled.", sqlState: null, errorCode: 0, queryId: Id);

                default:
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    delayMs = Math.Min(delayMs * 2, 5_000);
                    break;
            }
        }
    }

    private static IReadOnlyList<T> MapRows<T>(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var result = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            if (row is T typed)
            {
                result.Add(typed);
                continue;
            }

            var json = JsonSerializer.Serialize(row, RowSerializerOptions);
            var deserialized = JsonSerializer.Deserialize<T>(json, RowSerializerOptions)
                ?? throw new BricksNetException(
                    $"Failed to map a result row to type '{typeof(T).FullName}'.");
            result.Add(deserialized);
        }

        return result;
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new ObjectDisposedException(nameof(BricksNetOperation));
        }
    }
}
