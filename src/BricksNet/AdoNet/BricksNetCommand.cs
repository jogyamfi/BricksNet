using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using ContractParameter = BricksNet.Contracts.Options.BricksNetParameter;

namespace BricksNet.AdoNet;

/// <summary>
/// An ADO.NET command that executes a single SQL statement against a Databricks warehouse
/// or cluster through an open <see cref="BricksNetConnection"/>.
/// </summary>
public sealed class BricksNetCommand : DbCommand
{
    private readonly BricksNetParameterCollection _parameters = new();
    private BricksNetConnection? _connection;
    private IBricksNetOperation? _activeOperation;
    private int _commandTimeout;

    /// <summary>Initialises an empty command.</summary>
    public BricksNetCommand()
    {
    }

    /// <summary>Initialises a command with the given statement text.</summary>
    /// <param name="commandText">The SQL statement to execute.</param>
    public BricksNetCommand(string commandText) => CommandText = commandText;

    /// <summary>Initialises a command with statement text bound to a connection.</summary>
    /// <param name="commandText">The SQL statement to execute.</param>
    /// <param name="connection">The connection on which to execute.</param>
    public BricksNetCommand(string commandText, BricksNetConnection connection)
    {
        CommandText = commandText;
        _connection = connection;
    }

    /// <inheritdoc />
    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    /// <inheritdoc />
    public override int CommandTimeout
    {
        get => _commandTimeout;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "CommandTimeout cannot be negative.");
            }

            _commandTimeout = value;
        }
    }

    /// <summary>
    /// Command type. Only <see cref="CommandType.Text"/> is supported.
    /// </summary>
    /// <exception cref="NotSupportedException">A type other than <see cref="CommandType.Text"/> is assigned.</exception>
    public override CommandType CommandType
    {
        get => CommandType.Text;
        set
        {
            if (value != CommandType.Text)
            {
                throw new NotSupportedException("Only CommandType.Text is supported.");
            }
        }
    }

    /// <inheritdoc />
    public override bool DesignTimeVisible { get; set; }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    /// <inheritdoc />
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = value as BricksNetConnection
            ?? (value is null ? null : throw new InvalidCastException(
                $"A {nameof(BricksNetCommand)} requires a {nameof(BricksNetConnection)}."));
    }

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection => _parameters;

    /// <inheritdoc />
    protected override DbTransaction? DbTransaction { get; set; }

    /// <inheritdoc />
    public override void Cancel()
    {
        // Blocks on the async cancellation RPC. Calling Cancel() from the same synchronization
        // context that is awaiting an execute call can deadlock; prefer cancelling via the
        // CancellationToken passed to the *Async execute methods instead.
        _activeOperation?.CancelAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override void Prepare()
    {
        // Databricks SQL has no server-side prepared-statement handle; this is a no-op.
    }

    /// <inheritdoc />
    public override int ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        var session = GetOpenSession();
        var operation = await session
            .ExecuteStatementAsync(CommandText, BuildStatementOptions(), cancellationToken)
            .ConfigureAwait(false);
        _activeOperation = operation;

        await operation.CloseAsync().ConfigureAwait(false);
        return -1;
    }

    /// <inheritdoc />
    public override object? ExecuteScalar() => ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        var session = GetOpenSession();
        var operation = await session
            .ExecuteStatementAsync(CommandText, BuildStatementOptions(), cancellationToken)
            .ConfigureAwait(false);
        _activeOperation = operation;

        try
        {
            var rows = await operation
                .FetchChunkAsync<IReadOnlyDictionary<string, object?>>(1, cancellationToken)
                .ConfigureAwait(false);

            if (rows.Count == 0)
            {
                return null;
            }

            using var enumerator = rows[0].Values.GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }
        finally
        {
            await operation.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter() => new BricksNetParameter();

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        ExecuteDbDataReaderAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        var session = GetOpenSession();
        var chunkSize = ResolveChunkSize(behavior);
        var operation = await session
            .ExecuteStatementAsync(CommandText, BuildStatementOptions(), cancellationToken)
            .ConfigureAwait(false);
        _activeOperation = operation;

        IReadOnlyList<IReadOnlyDictionary<string, object?>> firstChunk =
            behavior.HasFlag(CommandBehavior.SchemaOnly)
                ? []
                : await operation
                    .FetchChunkAsync<IReadOnlyDictionary<string, object?>>(chunkSize, cancellationToken)
                    .ConfigureAwait(false);

        return new BricksNetDataReader(operation, firstChunk, chunkSize, behavior, _connection);
    }

    private int ResolveChunkSize(CommandBehavior behavior)
    {
        if (behavior.HasFlag(CommandBehavior.SingleRow))
        {
            return 1;
        }

        var maxRows = _connection?.EffectiveSessionOptions.MaxRows ?? 10_000;
        return maxRows <= 0 ? 10_000 : maxRows;
    }

    private StatementOptions BuildStatementOptions()
    {
        var sessionOptions = _connection?.EffectiveSessionOptions;
        return new StatementOptions
        {
            Catalog = sessionOptions?.Catalog,
            Schema = sessionOptions?.Schema,
            MaxRows = sessionOptions?.MaxRows ?? 10_000,
            QueryTimeoutSeconds = _commandTimeout,
            Parameters = BuildParameters(),
        };
    }

    private IReadOnlyList<ContractParameter>? BuildParameters()
    {
        if (_parameters.Count == 0)
        {
            return null;
        }

        var result = new List<ContractParameter>(_parameters.Count);
        foreach (BricksNetParameter parameter in _parameters)
        {
            result.Add(new ContractParameter(
                parameter.ParameterName.TrimStart('@', ':'),
                parameter.Value,
                parameter.TypeHint));
        }

        return result;
    }

    private IBricksNetSession GetOpenSession()
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("The command has no associated connection.");
        }

        if (string.IsNullOrWhiteSpace(CommandText))
        {
            throw new InvalidOperationException("CommandText has not been set.");
        }

        return _connection.GetOpenSession();
    }
}
