using System.Text;
using BricksNet.Backends.Rest.Dto;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;

namespace BricksNet.Backends.Rest;

/// <summary>
/// REST implementation of <see cref="ISessionBackend"/>. The SQL Statement Execution API has no
/// explicit session concept, so a synthetic session id is generated and the connection-level
/// <see cref="BackendSessionOptions.Catalog"/>/<see cref="BackendSessionOptions.Schema"/> defaults
/// are carried onto each request. Metadata lookups are issued as <c>SHOW</c> statements.
/// </summary>
internal sealed class RestSessionBackend : ISessionBackend
{
    // INLINE supports only JSON_ARRAY and aborts results over 25 MiB; EXTERNAL_LINKS (CloudFetch)
    // supports ARROW_STREAM and results up to 100 GiB.
    private const string InlineDisposition = "INLINE";
    private const string InlineFormat = "JSON_ARRAY";
    private const string ExternalLinksDisposition = "EXTERNAL_LINKS";
    private const string ExternalLinksFormat = "ARROW_STREAM";
    private const string OnWaitTimeout = "CONTINUE";
    // wait_timeout only bounds how long the POST blocks before returning a still-running statement
    // (5–50 s, or 0). It is not a query timeout; that is enforced by RestOperationBackend.
    private const int DefaultWaitSeconds = 10;
    private const int MinWaitSeconds = 5;

    private readonly SqlExecHttpClient _client;
    private readonly RestStatementPoller _poller;
    private readonly string _warehouseId;
    private readonly BackendSessionOptions _sessionOptions;
    private readonly bool _useCloudFetch;

    public RestSessionBackend(
        SqlExecHttpClient client,
        RestStatementPoller poller,
        string warehouseId,
        BackendSessionOptions sessionOptions,
        bool useCloudFetch = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _poller = poller ?? throw new ArgumentNullException(nameof(poller));
        _warehouseId = warehouseId ?? throw new ArgumentNullException(nameof(warehouseId));
        _sessionOptions = sessionOptions ?? throw new ArgumentNullException(nameof(sessionOptions));
        _useCloudFetch = useCloudFetch;
        Id = Guid.NewGuid().ToString();
    }

    public string Id { get; }

    public async Task<IOperationBackend> ExecuteStatementAsync(
        string sql,
        StatementOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);

        var timeoutSeconds = EffectiveTimeoutSeconds(options);
        var request = BuildRequest(sql, options);
        var response = await _client.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return new RestOperationBackend(
            _client,
            _poller,
            response,
            timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null);
    }

    public Task<IOperationBackend> GetCatalogsAsync(CancellationToken cancellationToken = default) =>
        ExecuteShowAsync("SHOW CATALOGS", cancellationToken);

    public Task<IOperationBackend> GetSchemasAsync(
        string? catalogName,
        string? schemaNamePattern,
        CancellationToken cancellationToken = default)
    {
        var sql = new StringBuilder("SHOW SCHEMAS");
        if (!string.IsNullOrEmpty(catalogName))
        {
            sql.Append(" IN ").Append(QuoteIdentifier(catalogName));
        }

        AppendLike(sql, schemaNamePattern);
        return ExecuteShowAsync(sql.ToString(), cancellationToken);
    }

    public Task<IOperationBackend> GetTablesAsync(
        string? catalogName,
        string? schemaNamePattern,
        string? tableNamePattern,
        CancellationToken cancellationToken = default)
    {
        var sql = new StringBuilder("SHOW TABLES");
        var qualified = JoinQualified(catalogName, schemaNamePattern);
        if (qualified.Length > 0)
        {
            sql.Append(" IN ").Append(qualified);
        }

        AppendLike(sql, tableNamePattern);
        return ExecuteShowAsync(sql.ToString(), cancellationToken);
    }

    public Task<IOperationBackend> GetColumnsAsync(
        string? catalogName,
        string? schemaNamePattern,
        string? tableNamePattern,
        string? columnNamePattern,
        CancellationToken cancellationToken = default)
    {
        // SHOW COLUMNS requires a concrete table; column-name filtering is not supported by the
        // statement and is left to higher layers.
        _ = columnNamePattern;

        var table = JoinQualified(catalogName, schemaNamePattern, tableNamePattern);
        if (table.Length == 0)
        {
            throw new ArgumentException(
                "A table name is required to list columns.", nameof(tableNamePattern));
        }

        return ExecuteShowAsync($"SHOW COLUMNS IN {table}", cancellationToken);
    }

    /// <summary>No-op: the SQL Statement Execution API has no server-side session to close.</summary>
    public Task CloseSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private async Task<IOperationBackend> ExecuteShowAsync(string sql, CancellationToken cancellationToken)
    {
        var request = BuildRequest(sql, options: null);
        var response = await _client.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return new RestOperationBackend(_client, _poller, response);
    }

    private ExecuteStatementRequest BuildRequest(string sql, StatementOptions? options)
    {
        // MaxRows is a client-side page size, not a server-side row cap, so row_limit is never
        // sent: doing so would silently truncate every result set to MaxRows rows.
        var timeoutSeconds = EffectiveTimeoutSeconds(options);

        return new ExecuteStatementRequest
        {
            WarehouseId = _warehouseId,
            Statement = sql,
            Catalog = options?.Catalog ?? _sessionOptions.Catalog,
            Schema = options?.Schema ?? _sessionOptions.Schema,
            Parameters = MapParameters(options?.Parameters),
            Disposition = _useCloudFetch ? ExternalLinksDisposition : InlineDisposition,
            Format = _useCloudFetch ? ExternalLinksFormat : InlineFormat,
            WaitTimeout = FormatWaitTimeout(timeoutSeconds),
            OnWaitTimeout = OnWaitTimeout,
        };
    }

    private static IReadOnlyList<StatementParameterDto>? MapParameters(
        IReadOnlyList<BricksNetParameter>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var mapped = new List<StatementParameterDto>(parameters.Count);
        foreach (var parameter in parameters)
        {
            var (value, type) = ParameterFormatter.Format(parameter);
            mapped.Add(new StatementParameterDto
            {
                Name = parameter.Name.TrimStart(':'),
                Value = value,
                Type = type,
            });
        }

        return mapped;
    }

    private int EffectiveTimeoutSeconds(StatementOptions? options) =>
        options is { QueryTimeoutSeconds: > 0 } ? options.QueryTimeoutSeconds : _sessionOptions.QueryTimeoutSeconds;

    // Never block the initial POST longer than the query timeout allows (subject to the API minimum).
    private static string FormatWaitTimeout(int timeoutSeconds)
    {
        var wait = timeoutSeconds > 0
            ? Math.Clamp(timeoutSeconds, MinWaitSeconds, DefaultWaitSeconds)
            : DefaultWaitSeconds;
        return $"{wait}s";
    }

    private static void AppendLike(StringBuilder sql, string? pattern)
    {
        if (!string.IsNullOrEmpty(pattern))
        {
            // SHOW ... LIKE uses '*' as its wildcard; translate the JDBC/SQL '%' wildcard so the same
            // pattern works on both backends.
            sql.Append(" LIKE ").Append(QuoteLiteral(pattern.Replace('%', '*')));
        }
    }

    private static string JoinQualified(params string?[] parts)
    {
        var quoted = parts
            .Where(part => !string.IsNullOrEmpty(part))
            .Select(part => QuoteIdentifier(part!));
        return string.Join('.', quoted);
    }

    private static string QuoteIdentifier(string identifier) =>
        $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    private static string QuoteLiteral(string literal) =>
        $"'{literal.Replace("'", "''", StringComparison.Ordinal)}'";
}
