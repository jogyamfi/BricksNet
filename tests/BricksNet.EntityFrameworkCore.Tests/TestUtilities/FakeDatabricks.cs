using BricksNet.AdoNet;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Results;

namespace BricksNet.EntityFrameworkCore.TestUtilities;

/// <summary>A statement the fake service received: its SQL text and bound parameters.</summary>
internal sealed record RecordedStatement(string Sql, IReadOnlyList<Contracts.Options.BricksNetParameter> Parameters);

/// <summary>A scripted result: the reported schema and the rows to return.</summary>
internal sealed record FakeResult(
    IReadOnlyList<ColumnInfo> Schema,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows)
{
    public static FakeResult Empty { get; } = new([], []);

    /// <summary>The <c>num_affected_rows</c> summary Databricks returns for DML.</summary>
    public static FakeResult AffectedRows(long count) => new(
        [new ColumnInfo("num_affected_rows", "BIGINT", 0, IsNullable: true, Precision: null, Scale: null)],
        [new Dictionary<string, object?> { ["num_affected_rows"] = count }]);

    /// <summary>A result with the given columns (name and Databricks type) and rows of values in column order.</summary>
    public static FakeResult Table((string Name, string Type)[] columns, params object?[][] rows)
    {
        var schema = columns
            .Select((column, ordinal) => new ColumnInfo(column.Name, column.Type, ordinal, IsNullable: true, Precision: null, Scale: null))
            .ToArray();
        var dictionaries = rows
            .Select(row =>
            {
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < columns.Length; i++)
                {
                    dictionary[columns[i].Name] = row[i];
                }

                return (IReadOnlyDictionary<string, object?>)dictionary;
            })
            .ToArray();
        return new FakeResult(schema, dictionaries);
    }
}

/// <summary>
/// An in-memory stand-in for a Databricks SQL warehouse. Records every statement executed through
/// connections it creates and answers each with <see cref="Responder"/>.
/// </summary>
internal sealed class FakeDatabricks
{
    public const string ConnectionString = "Host=fake.local;HttpPath=/sql/1.0/warehouses/fake;Token=tok;Catalog=main;Schema=default";

    private readonly List<RecordedStatement> _statements = [];

    /// <summary>Produces the result for each statement. Defaults to an empty result.</summary>
    public Func<RecordedStatement, FakeResult> Responder { get; set; } = _ => FakeResult.Empty;

    public IReadOnlyList<RecordedStatement> Statements
    {
        get
        {
            lock (_statements)
            {
                return [.. _statements];
            }
        }
    }

    public int SessionsOpened { get; private set; }

    /// <summary>Creates a connection whose client talks to this fake.</summary>
    public BricksNetConnection CreateConnection(string connectionString = ConnectionString) =>
        new(connectionString, (_, _) => new Client(this));

    private void Record(RecordedStatement statement)
    {
        lock (_statements)
        {
            _statements.Add(statement);
        }
    }

    private sealed class Client(FakeDatabricks service) : IBricksNetClient
    {
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IBricksNetSession> OpenSessionAsync(SessionOptions? options = null, CancellationToken cancellationToken = default)
        {
            service.SessionsOpened++;
            return Task.FromResult<IBricksNetSession>(new Session(service));
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Session(FakeDatabricks service) : IBricksNetSession
    {
        public Task<IBricksNetOperation> ExecuteStatementAsync(
            string sql,
            StatementOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var statement = new RecordedStatement(sql, options?.Parameters ?? []);
            service.Record(statement);
            return Task.FromResult<IBricksNetOperation>(new Operation(service.Responder(statement)));
        }

        public Task<IBricksNetOperation> GetCatalogsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IBricksNetOperation> GetSchemasAsync(
            string? catalogName = null,
            string? schemaNamePattern = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IBricksNetOperation> GetTablesAsync(
            string? catalogName = null,
            string? schemaNamePattern = null,
            string? tableNamePattern = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IBricksNetOperation> GetColumnsAsync(
            string? catalogName = null,
            string? schemaNamePattern = null,
            string? tableNamePattern = null,
            string? columnNamePattern = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Operation(FakeResult result) : IBricksNetOperation
    {
        private int _cursor;

        public Task<IReadOnlyList<T>> FetchAllAsync<T>(CancellationToken cancellationToken = default) =>
            FetchChunkAsync<T>(int.MaxValue, cancellationToken);

        public Task<IReadOnlyList<T>> FetchChunkAsync<T>(int maxRows, CancellationToken cancellationToken = default)
        {
            var chunk = result.Rows.Skip(_cursor).Take(maxRows).Cast<T>().ToArray();
            _cursor += chunk.Length;
            return Task.FromResult<IReadOnlyList<T>>(chunk);
        }

        public Task WaitForCompletionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ColumnInfo>> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result.Schema);

        public Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(QueryStatus.Succeeded);

        public Task CancelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
