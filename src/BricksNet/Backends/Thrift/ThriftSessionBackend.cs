using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Backends.Thrift.Generated;

namespace BricksNet.Backends.Thrift;

/// <summary>
/// Thrift implementation of <see cref="ISessionBackend"/>. Maps each session-scoped
/// call onto a TCLIService RPC, replaying the server-assigned session handle.
/// </summary>
internal sealed class ThriftSessionBackend : ISessionBackend
{
    private readonly TCLIServiceClient _client;
    private readonly TSessionHandle _sessionHandle;
    private readonly BackendSessionOptions _options;
    private bool _closed;

    public ThriftSessionBackend(
        TCLIServiceClient client,
        TSessionHandle sessionHandle,
        BackendSessionOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionHandle = sessionHandle ?? throw new ArgumentNullException(nameof(sessionHandle));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Id = ThriftHandle.ToId(sessionHandle.SessionId?.Guid);
    }

    public string Id { get; }

    public async Task<IOperationBackend> ExecuteStatementAsync(
        string sql,
        StatementOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var timeoutSeconds = options is { QueryTimeoutSeconds: > 0 }
            ? options.QueryTimeoutSeconds
            : _options.QueryTimeoutSeconds;

        var request = new TExecuteStatementReq
        {
            SessionHandle = _sessionHandle,
            Statement = sql,
            RunAsync = true,
            QueryTimeout = timeoutSeconds > 0 ? timeoutSeconds : null,
            ConfOverlay = options?.QueryTags ?? _options.QueryTags,
            Parameters = MapParameters(options?.Parameters),
        };

        var response = await _client.ExecuteStatementAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status);

        return CreateOperation(response.OperationHandle);
    }

    public async Task<IOperationBackend> GetCatalogsAsync(CancellationToken cancellationToken = default)
    {
        var request = new TGetCatalogsReq { SessionHandle = _sessionHandle, RunAsync = true };
        var response = await _client.GetCatalogsAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status);
        return CreateOperation(response.OperationHandle);
    }

    public async Task<IOperationBackend> GetSchemasAsync(
        string? catalogName,
        string? schemaNamePattern,
        CancellationToken cancellationToken = default)
    {
        var request = new TGetSchemasReq
        {
            SessionHandle = _sessionHandle,
            CatalogName = catalogName,
            SchemaName = schemaNamePattern,
            RunAsync = true,
        };
        var response = await _client.GetSchemasAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status);
        return CreateOperation(response.OperationHandle);
    }

    public async Task<IOperationBackend> GetTablesAsync(
        string? catalogName,
        string? schemaNamePattern,
        string? tableNamePattern,
        CancellationToken cancellationToken = default)
    {
        var request = new TGetTablesReq
        {
            SessionHandle = _sessionHandle,
            CatalogName = catalogName,
            SchemaName = schemaNamePattern,
            TableName = tableNamePattern,
            RunAsync = true,
        };
        var response = await _client.GetTablesAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status);
        return CreateOperation(response.OperationHandle);
    }

    public async Task<IOperationBackend> GetColumnsAsync(
        string? catalogName,
        string? schemaNamePattern,
        string? tableNamePattern,
        string? columnNamePattern,
        CancellationToken cancellationToken = default)
    {
        var request = new TGetColumnsReq
        {
            SessionHandle = _sessionHandle,
            CatalogName = catalogName,
            SchemaName = schemaNamePattern,
            TableName = tableNamePattern,
            ColumnName = columnNamePattern,
            RunAsync = true,
        };
        var response = await _client.GetColumnsAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status);
        return CreateOperation(response.OperationHandle);
    }

    public async Task CloseSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        var request = new TCloseSessionReq { SessionHandle = _sessionHandle };
        var response = await _client.CloseSessionAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status);
    }

    private ThriftOperationBackend CreateOperation(TOperationHandle? handle)
    {
        if (handle is null)
        {
            throw new InvalidOperationException("The operation succeeded but returned no operation handle.");
        }

        return new ThriftOperationBackend(_client, handle);
    }

    private static IReadOnlyList<TSparkParameter>? MapParameters(IReadOnlyList<BricksNetParameter>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var mapped = new List<TSparkParameter>(parameters.Count);
        foreach (var parameter in parameters)
        {
            // Send every value as a string with an explicit type (as the JDBC driver does), so
            // BIGINT and DECIMAL values keep full precision instead of passing through double.
            var (value, type) = ParameterFormatter.Format(parameter);
            mapped.Add(new TSparkParameter
            {
                Name = parameter.Name.TrimStart(':'),
                Type = type,
                Value = value is null ? new TSparkParameterValue() : new TSparkParameterValue { StringValue = value },
            });
        }

        return mapped;
    }
}
