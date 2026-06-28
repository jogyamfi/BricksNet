using System.Globalization;
using System.Text;
using System.Text.Json;
using BricksNet.AdoNet;
using BricksNet.Backends.Rest.Dto;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Results;

namespace BricksNet.Backends.Rest;

/// <summary>
/// REST implementation of <see cref="IOperationBackend"/> over the SQL Statement Execution API.
/// Waits for the statement to reach a terminal state (reusing the embedded direct result when
/// available), then materialises inline Arrow/JSON payloads or CloudFetch external links.
/// </summary>
internal sealed class RestOperationBackend : IOperationBackend
{
    private readonly SqlExecHttpClient _client;
    private readonly RestStatementPoller _poller;

    private ExecuteStatementResponse? _terminalResponse;

    public RestOperationBackend(
        SqlExecHttpClient client,
        RestStatementPoller poller,
        ExecuteStatementResponse initialResponse)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _poller = poller ?? throw new ArgumentNullException(nameof(poller));
        ArgumentNullException.ThrowIfNull(initialResponse);

        Id = initialResponse.StatementId;
        if (RestStatementPoller.IsTerminal(initialResponse.Status.State))
        {
            _terminalResponse = initialResponse;
        }
    }

    public string Id { get; }

    public async Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_terminalResponse is not null)
        {
            return MapState(_terminalResponse.Status.State);
        }

        var response = await _client.GetStatementAsync(Id, cancellationToken).ConfigureAwait(false);
        if (RestStatementPoller.IsTerminal(response.Status.State))
        {
            _terminalResponse = response;
        }

        return MapState(response.Status.State);
    }

    public async Task<FetchResult> FetchResultsAsync(FetchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var cancellationToken = options.CancellationToken;

        var response = _terminalResponse
            ??= await _poller.PollUntilTerminalAsync(Id, cancellationToken).ConfigureAwait(false);

        EnsureNotFailed(response);

        var schema = BuildSchema(response.Manifest);
        return BuildFetchResult(response.Result, response.Manifest, schema);
    }

    public Task CancelAsync(CancellationToken cancellationToken = default) =>
        _client.CancelAsync(Id, cancellationToken);

    /// <summary>No-op: SQL Statement Execution API statements close automatically.</summary>
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private void EnsureNotFailed(ExecuteStatementResponse response)
    {
        if (response.Status.State != StatementState.Failed)
        {
            return;
        }

        var error = response.Status.Error;
        var message = error?.Message ?? "The statement failed without an error message.";
        throw new BricksNetException(message, sqlState: null, errorCode: 0, queryId: Id);
    }

    private static FetchResult BuildFetchResult(
        ResultData? result,
        ResultManifest? manifest,
        IReadOnlyList<ColumnInfo> schema)
    {
        if (result is null)
        {
            return new FetchResult(ResultFormat.Arrow, ReadOnlyMemory<byte>.Empty, ExternalLinks: null, schema);
        }

        if (result.ExternalLinks is { Count: > 0 } links)
        {
            return new FetchResult(ResultFormat.CloudFetch, ReadOnlyMemory<byte>.Empty, MapLinks(links), schema);
        }

        return BuildInlineResult(result, manifest, schema);
    }

    private static FetchResult BuildInlineResult(
        ResultData result,
        ResultManifest? manifest,
        IReadOnlyList<ColumnInfo> schema)
    {
        if (result.Data is not { } data ||
            data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new FetchResult(ResultFormat.Arrow, ReadOnlyMemory<byte>.Empty, ExternalLinks: null, schema);
        }

        // ARROW_STREAM inline payloads arrive base64-encoded as a JSON string.
        if (data.ValueKind == JsonValueKind.String)
        {
            var base64 = data.GetString() ?? string.Empty;
            ReadOnlyMemory<byte> arrow = base64.Length == 0
                ? ReadOnlyMemory<byte>.Empty
                : Convert.FromBase64String(base64);
            return new FetchResult(ResultFormat.Arrow, arrow, ExternalLinks: null, schema);
        }

        // JSON_ARRAY inline payloads arrive as a JSON array of row arrays.
        var format = MapInlineFormat(manifest?.Format);
        var payload = Encoding.UTF8.GetBytes(data.GetRawText());
        return new FetchResult(format, payload, ExternalLinks: null, schema);
    }

    private static IReadOnlyList<ColumnInfo> BuildSchema(ResultManifest? manifest)
    {
        var columns = manifest?.Schema?.Columns;
        if (columns is null || columns.Count == 0)
        {
            return Array.Empty<ColumnInfo>();
        }

        var schema = new List<ColumnInfo>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            schema.Add(new ColumnInfo(
                Name: column.Name ?? string.Empty,
                TypeName: column.TypeName ?? column.TypeText ?? "STRING",
                Ordinal: column.Position,
                IsNullable: true,
                Precision: column.TypePrecision,
                Scale: column.TypeScale));
        }

        return schema;
    }

    private static IReadOnlyList<ExternalLink> MapLinks(IReadOnlyList<ExternalLinkDto> links)
    {
        var mapped = new List<ExternalLink>(links.Count);
        foreach (var link in links)
        {
            mapped.Add(new ExternalLink(
                Uri: new Uri(link.ExternalLink ?? throw new BricksNetException(
                    "A CloudFetch external link had no URL.")),
                FileSizeBytes: link.ByteCount,
                Expiration: ParseExpiration(link.Expiration),
                ChunkIndex: link.ChunkIndex,
                RowOffset: link.RowOffset,
                RowCount: link.RowCount,
                HttpHeaders: link.HttpHeaders));
        }

        return mapped;
    }

    private static DateTimeOffset ParseExpiration(string? expiration) =>
        DateTimeOffset.TryParse(
            expiration,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.MaxValue;

    private static ResultFormat MapInlineFormat(string? manifestFormat) =>
        string.Equals(manifestFormat, "ARROW_STREAM", StringComparison.OrdinalIgnoreCase)
            ? ResultFormat.Arrow
            : ResultFormat.JsonColumnar;

    private static QueryStatus MapState(StatementState state) => state switch
    {
        StatementState.Pending => QueryStatus.Pending,
        StatementState.Running => QueryStatus.Running,
        StatementState.Succeeded => QueryStatus.Succeeded,
        StatementState.Failed => QueryStatus.Failed,
        StatementState.Canceled => QueryStatus.Cancelled,
        StatementState.Closed => QueryStatus.Closed,
        _ => QueryStatus.Pending,
    };
}
