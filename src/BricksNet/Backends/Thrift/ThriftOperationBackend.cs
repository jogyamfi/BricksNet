using BricksNet.AdoNet;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Backends.Thrift.Generated;
using BricksNet.Results;

namespace BricksNet.Backends.Thrift;

/// <summary>
/// Thrift implementation of <see cref="IOperationBackend"/>. Stores the opaque
/// <see cref="TOperationHandle"/> and replays it on every subsequent RPC. Result
/// fetching materialises Arrow inline batches and CloudFetch links; legacy
/// row/column result sets are reported as unsupported (refined in Phase 7).
/// </summary>
internal sealed class ThriftOperationBackend : IOperationBackend
{
    private readonly TCLIServiceClient _client;
    private readonly TOperationHandle _operationHandle;

    private TGetResultSetMetadataResp? _metadata;
    private IReadOnlyList<ColumnInfo>? _schema;
    private bool _closed;

    public ThriftOperationBackend(TCLIServiceClient client, TOperationHandle operationHandle)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _operationHandle = operationHandle ?? throw new ArgumentNullException(nameof(operationHandle));
        Id = ThriftHandle.ToId(operationHandle.OperationId?.Guid);
    }

    public string Id { get; }

    public async Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var request = new TGetOperationStatusReq { OperationHandle = _operationHandle };
        var response = await _client.GetOperationStatusAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status, Id);
        return MapState(response.OperationState);
    }

    public async Task<FetchResult> FetchResultsAsync(FetchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var cancellationToken = options.CancellationToken;

        var metadata = await EnsureMetadataAsync(cancellationToken).ConfigureAwait(false);
        var schema = _schema ??= BuildSchema(metadata);

        var request = new TFetchResultsReq
        {
            OperationHandle = _operationHandle,
            Orientation = TFetchOrientation.FetchNext,
            MaxRows = options.MaxRows,
            FetchType = 0,
        };

        var response = await _client.FetchResultsAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status, Id);

        var rowSet = response.Results;

        if (rowSet?.ResultLinks is { Count: > 0 } links)
        {
            return new FetchResult(ResultFormat.CloudFetch, ReadOnlyMemory<byte>.Empty, MapLinks(links), schema);
        }

        if (rowSet?.ArrowBatches is { Count: > 0 } batches)
        {
            return new FetchResult(ResultFormat.Arrow, BuildArrowPayload(metadata, batches), null, schema);
        }

        if (rowSet?.HasColumnarData == true)
        {
            throw new BricksNetException(
                "The Databricks server returned a legacy row/column result set, which the Thrift backend does not support. " +
                "Ensure the warehouse supports Arrow results.");
        }

        // No rows in this chunk.
        return new FetchResult(ResultFormat.Arrow, ReadOnlyMemory<byte>.Empty, null, schema);
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        var request = new TCancelOperationReq { OperationHandle = _operationHandle };
        var response = await _client.CancelOperationAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status, Id);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        var request = new TCloseOperationReq { OperationHandle = _operationHandle };
        var response = await _client.CloseOperationAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status, Id);
    }

    private async Task<TGetResultSetMetadataResp> EnsureMetadataAsync(CancellationToken cancellationToken)
    {
        if (_metadata is not null)
        {
            return _metadata;
        }

        var request = new TGetResultSetMetadataReq { OperationHandle = _operationHandle };
        var response = await _client.GetResultSetMetadataAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status, Id);
        _metadata = response;
        return response;
    }

    private static IReadOnlyList<ColumnInfo> BuildSchema(TGetResultSetMetadataResp metadata)
    {
        var columns = metadata.Schema?.Columns;
        if (columns is null || columns.Count == 0)
        {
            return Array.Empty<ColumnInfo>();
        }

        var schema = new List<ColumnInfo>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var typeName = column.TypeDesc?.PrimitiveType.ToSqlTypeName() ?? "STRING";
            schema.Add(new ColumnInfo(
                Name: column.ColumnName ?? string.Empty,
                TypeName: typeName,
                Ordinal: i,
                IsNullable: true,
                Precision: null,
                Scale: null));
        }

        return schema;
    }

    private static IReadOnlyList<ExternalLink> MapLinks(IReadOnlyList<TSparkArrowResultLink> links)
    {
        var mapped = new List<ExternalLink>(links.Count);
        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            mapped.Add(new ExternalLink(
                Uri: new Uri(link.FileLink ?? throw new BricksNetException("A CloudFetch link had no file URL.")),
                FileSizeBytes: link.BytesNum,
                Expiration: DateTimeOffset.FromUnixTimeMilliseconds(link.ExpiryTime),
                ChunkIndex: i,
                RowOffset: link.StartRowOffset,
                RowCount: link.RowCount,
                HttpHeaders: link.HttpHeaders));
        }

        return mapped;
    }

    private static ReadOnlyMemory<byte> BuildArrowPayload(
        TGetResultSetMetadataResp metadata,
        IReadOnlyList<TSparkArrowBatch> batches)
    {
        var schemaBytes = metadata.ArrowSchema ?? Array.Empty<byte>();
        var totalLength = schemaBytes.Length;
        foreach (var batch in batches)
        {
            totalLength += batch.Batch?.Length ?? 0;
        }

        var payload = new byte[totalLength];
        var offset = 0;
        if (schemaBytes.Length > 0)
        {
            Buffer.BlockCopy(schemaBytes, 0, payload, offset, schemaBytes.Length);
            offset += schemaBytes.Length;
        }

        foreach (var batch in batches)
        {
            if (batch.Batch is { Length: > 0 } data)
            {
                Buffer.BlockCopy(data, 0, payload, offset, data.Length);
                offset += data.Length;
            }
        }

        return payload;
    }

    private static QueryStatus MapState(TOperationState state) => state switch
    {
        TOperationState.InitializedState => QueryStatus.Pending,
        TOperationState.PendingState => QueryStatus.Pending,
        TOperationState.RunningState => QueryStatus.Running,
        TOperationState.FinishedState => QueryStatus.Succeeded,
        TOperationState.CanceledState => QueryStatus.Cancelled,
        TOperationState.ClosedState => QueryStatus.Closed,
        TOperationState.ErrorState => QueryStatus.Failed,
        TOperationState.TimedoutState => QueryStatus.Failed,
        _ => QueryStatus.Pending,
    };
}
