using System.Diagnostics;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Telemetry;
using Microsoft.Extensions.Logging;

namespace BricksNet;

/// <summary>
/// Fluent session over an open Databricks SQL warehouse or cluster. Wraps an
/// <see cref="ISessionBackend"/> and applies the session-level defaults to every statement.
/// Mirrors <c>DBSQLSession</c> from <c>databricks-sql-nodejs/lib/DBSQLSession.ts</c>.
/// </summary>
public sealed class BricksNetSession : IBricksNetSession
{
    private readonly ISessionBackend _backend;
    private readonly SessionOptions _defaults;
    private readonly BricksNetClientOptions _clientOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BricksNetSession> _logger;
    private readonly string _backendName;
    private readonly Action<BricksNetSession>? _onClosed;

    private bool _closed;

    internal BricksNetSession(
        ISessionBackend backend,
        SessionOptions defaults,
        BricksNetClientOptions clientOptions,
        string backendName,
        ILoggerFactory loggerFactory,
        Action<BricksNetSession>? onClosed)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        _clientOptions = clientOptions ?? throw new ArgumentNullException(nameof(clientOptions));
        _backendName = backendName ?? throw new ArgumentNullException(nameof(backendName));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<BricksNetSession>();
        _onClosed = onClosed;
    }

    /// <summary>The server-assigned session identifier.</summary>
    public string Id => _backend.Id;

    /// <inheritdoc />
    public async Task<IBricksNetOperation> ExecuteStatementAsync(
        string sql,
        StatementOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);
        ThrowIfClosed();

        var effective = MergeOptions(options);

        using Activity? activity = BricksNetActivitySource.StartStatementExecute(
            sql,
            effective.Catalog,
            effective.Schema,
            _clientOptions.Host,
            _clientOptions.Port,
            _backendName,
            _clientOptions.TelemetryOptions.SensitiveDataLoggingEnabled);

        LogMessages.StatementExecuting(_logger, sql.Length, _backendName);

        var operationBackend = await _backend
            .ExecuteStatementAsync(sql, effective, cancellationToken)
            .ConfigureAwait(false);

        BricksNetActivitySource.SetStatementId(activity, operationBackend.Id);

        return CreateOperation(operationBackend, effective.MaxRows);
    }

    /// <inheritdoc />
    public async Task<IBricksNetOperation> GetCatalogsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        var operationBackend = await _backend.GetCatalogsAsync(cancellationToken).ConfigureAwait(false);
        return CreateOperation(operationBackend, _defaults.MaxRows);
    }

    /// <inheritdoc />
    public async Task<IBricksNetOperation> GetSchemasAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        var operationBackend = await _backend
            .GetSchemasAsync(catalogName, schemaNamePattern, cancellationToken)
            .ConfigureAwait(false);
        return CreateOperation(operationBackend, _defaults.MaxRows);
    }

    /// <inheritdoc />
    public async Task<IBricksNetOperation> GetTablesAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        string? tableNamePattern = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        var operationBackend = await _backend
            .GetTablesAsync(catalogName, schemaNamePattern, tableNamePattern, cancellationToken)
            .ConfigureAwait(false);
        return CreateOperation(operationBackend, _defaults.MaxRows);
    }

    /// <inheritdoc />
    public async Task<IBricksNetOperation> GetColumnsAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        string? tableNamePattern = null,
        string? columnNamePattern = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        var operationBackend = await _backend
            .GetColumnsAsync(catalogName, schemaNamePattern, tableNamePattern, columnNamePattern, cancellationToken)
            .ConfigureAwait(false);
        return CreateOperation(operationBackend, _defaults.MaxRows);
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        var sessionId = Id;
        await _backend.CloseSessionAsync(cancellationToken).ConfigureAwait(false);
        LogMessages.SessionClosed(_logger, sessionId);
        _onClosed?.Invoke(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }

    private BricksNetOperation CreateOperation(IOperationBackend operationBackend, int maxRows) =>
        new(operationBackend, maxRows, _backendName, _loggerFactory);

    private StatementOptions MergeOptions(StatementOptions? options)
    {
        var baseOptions = options ?? new StatementOptions();
        return baseOptions with
        {
            Catalog = baseOptions.Catalog ?? _defaults.Catalog,
            Schema = baseOptions.Schema ?? _defaults.Schema,
            QueryTimeoutSeconds = baseOptions.QueryTimeoutSeconds > 0
                ? baseOptions.QueryTimeoutSeconds
                : _defaults.QueryTimeoutSeconds,
            MaxRows = baseOptions.MaxRows > 0 ? baseOptions.MaxRows : _defaults.MaxRows,
            QueryTags = baseOptions.QueryTags ?? _defaults.QueryTags,
        };
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new ObjectDisposedException(nameof(BricksNetSession));
        }
    }
}
