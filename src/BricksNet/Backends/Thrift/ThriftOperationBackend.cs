using Apache.Arrow;
using Apache.Arrow.Ipc;
using BricksNet.AdoNet;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Backends.Thrift.Generated;
using BricksNet.Results;

namespace BricksNet.Backends.Thrift;

/// <summary>
/// Thrift implementation of <see cref="IOperationBackend"/>. Stores the opaque
/// <see cref="TOperationHandle"/> and replays it on every subsequent RPC. Result
/// fetching materialises Arrow inline batches, CloudFetch links and the column-based
/// sets returned by metadata operations; row-based result sets are reported as unsupported.
/// </summary>
internal sealed class ThriftOperationBackend : IOperationBackend, IOperationFailureSource
{
    private readonly TCLIServiceClient _client;
    private readonly TOperationHandle _operationHandle;

    private TGetOperationStatusResp? _failedStatus;
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

        var state = MapState(response.OperationState);
        if (state == QueryStatus.Failed)
        {
            _failedStatus = response;
        }

        return state;
    }

    public BricksNetException? CreateFailureException()
    {
        if (_failedStatus is not { } status)
        {
            return null;
        }

        var message = !string.IsNullOrWhiteSpace(status.DisplayMessage) ? status.DisplayMessage
            : !string.IsNullOrWhiteSpace(status.ErrorMessage) ? status.ErrorMessage
            : status.OperationState == TOperationState.TimedoutState ? "The statement timed out."
            : "The statement failed without an error message.";

        return new BricksNetException(message, status.SqlState, status.ErrorCode ?? 0, Id);
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
        var hasMoreRows = response.HasMoreRows;

        if (rowSet?.ResultLinks is { Count: > 0 } links)
        {
            return new FetchResult(
                ResultFormat.CloudFetch, ReadOnlyMemory<byte>.Empty, MapLinks(links), schema, hasMoreRows, metadata.Lz4Compressed);
        }

        if (rowSet?.ArrowBatches is { Count: > 0 } batches)
        {
            return new FetchResult(ResultFormat.Arrow, BuildArrowPayload(metadata, batches), null, schema, hasMoreRows);
        }

        if (rowSet?.Columns is { Count: > 0 } columns && columns[0].Values.Count > 0)
        {
            var payload = await BuildArrowPayloadAsync(metadata, columns, cancellationToken).ConfigureAwait(false);
            return new FetchResult(ResultFormat.Arrow, payload, null, schema, hasMoreRows);
        }

        if (rowSet?.HasRowData == true)
        {
            throw new BricksNetException(
                "The Databricks server returned a row-based result set, which the Thrift backend does not support. " +
                "Ensure the warehouse supports Arrow results.");
        }

        // No rows in this chunk.
        return new FetchResult(ResultFormat.Arrow, ReadOnlyMemory<byte>.Empty, null, schema, hasMoreRows);
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
            return System.Array.Empty<ColumnInfo>();
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

    // Arrow IPC end-of-stream marker: continuation token (0xFFFFFFFF) followed by size 0.
    // ArrowStreamReader requires this sentinel to return null instead of throwing on EOF.
    private static ReadOnlySpan<byte> ArrowEos => [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];

    private static ReadOnlyMemory<byte> BuildArrowPayload(
        TGetResultSetMetadataResp metadata,
        IReadOnlyList<TSparkArrowBatch> batches)
    {
        var schemaBytes = metadata.ArrowSchema ?? System.Array.Empty<byte>();

        // We advertise canDecompressLZ4Result, so each inline batch may be its own LZ4 frame.
        // The schema is never compressed.
        var batchBytes = new List<byte[]>(batches.Count);
        foreach (var batch in batches)
        {
            if (batch.Batch is { Length: > 0 } data)
            {
                batchBytes.Add(metadata.Lz4Compressed ? Lz4Decompressor.Decode(data) : data);
            }
        }

        var totalLength = schemaBytes.Length;
        foreach (var data in batchBytes)
        {
            totalLength += data.Length;
        }

        totalLength += ArrowEos.Length;

        var payload = new byte[totalLength];
        var offset = 0;
        if (schemaBytes.Length > 0)
        {
            Buffer.BlockCopy(schemaBytes, 0, payload, offset, schemaBytes.Length);
            offset += schemaBytes.Length;
        }

        foreach (var data in batchBytes)
        {
            Buffer.BlockCopy(data, 0, payload, offset, data.Length);
            offset += data.Length;
        }

        ArrowEos.CopyTo(payload.AsSpan(offset));

        return payload;
    }

    // Metadata operations return column-based sets. Re-encode them as an Arrow IPC stream so they
    // flow through the same ArrowResultHandler as statement results.
    private static async Task<ReadOnlyMemory<byte>> BuildArrowPayloadAsync(
        TGetResultSetMetadataResp metadata,
        IReadOnlyList<TColumn> columns,
        CancellationToken cancellationToken)
    {
        var names = metadata.Schema?.Columns;
        var rowCount = columns[0].Values.Count;
        var schemaBuilder = new Schema.Builder();
        var arrays = new List<IArrowArray>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            var array = ToArrowArray(columns[i]);
            var name = names is not null && i < names.Count ? names[i].ColumnName : null;
            schemaBuilder.Field(new Field(name ?? $"col{i}", array.Data.DataType, nullable: true));
            arrays.Add(array);
        }

        var schema = schemaBuilder.Build();
        using var batch = new RecordBatch(schema, arrays, rowCount);
        using var stream = new MemoryStream();
        using (var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true))
        {
            await writer.WriteRecordBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            await writer.WriteEndAsync(cancellationToken).ConfigureAwait(false);
        }

        return stream.ToArray();
    }

    private static IArrowArray ToArrowArray(TColumn column) => column.Kind switch
    {
        TColumnKind.Bool => Build(column, new BooleanArray.Builder(), (b, v) => b.Append((bool)v), b => b.AppendNull(), b => b.Build()),
        TColumnKind.Byte => Build(column, new Int8Array.Builder(), (b, v) => b.Append((sbyte)v), b => b.AppendNull(), b => b.Build()),
        TColumnKind.I16 => Build(column, new Int16Array.Builder(), (b, v) => b.Append((short)v), b => b.AppendNull(), b => b.Build()),
        TColumnKind.I32 => Build(column, new Int32Array.Builder(), (b, v) => b.Append((int)v), b => b.AppendNull(), b => b.Build()),
        TColumnKind.I64 => Build(column, new Int64Array.Builder(), (b, v) => b.Append((long)v), b => b.AppendNull(), b => b.Build()),
        TColumnKind.Double => Build(column, new DoubleArray.Builder(), (b, v) => b.Append((double)v), b => b.AppendNull(), b => b.Build()),
        TColumnKind.String => Build(column, new StringArray.Builder(), (b, v) => b.Append((string)v), b => b.AppendNull(), b => b.Build()),
        _ => Build(column, new BinaryArray.Builder(), (b, v) => b.Append(((byte[])v).AsSpan()), b => b.AppendNull(), b => b.Build()),
    };

    private static IArrowArray Build<TBuilder>(
        TColumn column,
        TBuilder builder,
        Action<TBuilder, object> append,
        Action<TBuilder> appendNull,
        Func<TBuilder, IArrowArray> build)
    {
        for (var row = 0; row < column.Values.Count; row++)
        {
            if (column.IsNull(row))
            {
                appendNull(builder);
            }
            else
            {
                append(builder, column.Values[row]!);
            }
        }

        return build(builder);
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
