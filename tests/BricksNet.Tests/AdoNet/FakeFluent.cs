using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Results;

namespace BricksNet.AdoNet;

/// <summary>
/// Hand-rolled in-memory fakes implementing the fluent contracts, used to exercise the
/// ADO.NET adapter without a live Databricks endpoint.
/// </summary>
internal static class FakeFluent
{
    public sealed class FakeClient(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) : IBricksNetClient
    {
        public bool Connected { get; private set; }

        public bool Closed { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            Connected = true;
            return Task.CompletedTask;
        }

        public Task<IBricksNetSession> OpenSessionAsync(
            SessionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IBricksNetSession>(new FakeSession(rows));

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Closed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class FakeSession(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) : IBricksNetSession
    {
        public bool Closed { get; private set; }

        public Exception? WaitFailure { get; set; }

        public FakeOperation? LastOperation { get; private set; }

        public Task<IBricksNetOperation> ExecuteStatementAsync(
            string sql,
            StatementOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOperation = new FakeOperation(rows) { WaitFailure = WaitFailure };
            return Task.FromResult<IBricksNetOperation>(LastOperation);
        }

        public Task<IBricksNetOperation> GetCatalogsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IBricksNetOperation>(new FakeOperation(rows));

        public Task<IBricksNetOperation> GetSchemasAsync(
            string? catalogName = null,
            string? schemaNamePattern = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IBricksNetOperation>(new FakeOperation(rows));

        public Task<IBricksNetOperation> GetTablesAsync(
            string? catalogName = null,
            string? schemaNamePattern = null,
            string? tableNamePattern = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IBricksNetOperation>(new FakeOperation(rows));

        public Task<IBricksNetOperation> GetColumnsAsync(
            string? catalogName = null,
            string? schemaNamePattern = null,
            string? tableNamePattern = null,
            string? columnNamePattern = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IBricksNetOperation>(new FakeOperation(rows));

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Closed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class FakeOperation(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) : IBricksNetOperation
    {
        private int _cursor;

        public bool Closed { get; private set; }

        public bool Cancelled { get; private set; }

        public bool Waited { get; private set; }

        public Exception? WaitFailure { get; init; }

        public Task<IReadOnlyList<T>> FetchAllAsync<T>(CancellationToken cancellationToken = default)
        {
            var all = rows.Cast<T>().ToList();
            _cursor = rows.Count;
            return Task.FromResult<IReadOnlyList<T>>(all);
        }

        public Task<IReadOnlyList<T>> FetchChunkAsync<T>(int maxRows, CancellationToken cancellationToken = default)
        {
            var remaining = rows.Count - _cursor;
            var take = Math.Max(0, Math.Min(maxRows, remaining));
            var chunk = new List<T>(take);
            for (var i = 0; i < take; i++)
            {
                chunk.Add((T)rows[_cursor + i]);
            }

            _cursor += take;
            return Task.FromResult<IReadOnlyList<T>>(chunk);
        }

        public Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
        {
            Waited = true;
            return WaitFailure is null ? Task.CompletedTask : Task.FromException(WaitFailure);
        }

        public Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(QueryStatus.Succeeded);

        public Task CancelAsync(CancellationToken cancellationToken = default)
        {
            Cancelled = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Closed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(
        params IReadOnlyDictionary<string, object?>[] rows) => rows;

    public static IReadOnlyDictionary<string, object?> Row(params (string Name, object? Value)[] cells)
    {
        var dict = new Dictionary<string, object?>(cells.Length, StringComparer.Ordinal);
        foreach (var (name, value) in cells)
        {
            dict[name] = value;
        }

        return dict;
    }
}
