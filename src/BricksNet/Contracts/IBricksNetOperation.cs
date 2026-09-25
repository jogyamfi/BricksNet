using BricksNet.Results;

namespace BricksNet.Contracts;

/// <summary>
/// Handle to a submitted statement whose results can be consumed incrementally.
/// Mirrors <c>DBSQLOperation</c> from <c>databricks-sql-nodejs/lib/DBSQLOperation.ts</c>.
/// </summary>
public interface IBricksNetOperation : IAsyncDisposable
{
    /// <summary>
    /// Buffers and returns all result rows, mapping each row to <typeparamref name="T"/>.
    /// </summary>
    Task<IReadOnlyList<T>> FetchAllAsync<T>(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="maxRows"/> rows from the current cursor position,
    /// mapping each row to <typeparamref name="T"/>.
    /// </summary>
    Task<IReadOnlyList<T>> FetchChunkAsync<T>(int maxRows, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until the statement reaches a terminal state without fetching any rows.
    /// Throws <see cref="AdoNet.BricksNetException"/> carrying the server's error details when the
    /// statement failed or was cancelled.
    /// </summary>
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the result-set column metadata reported by the server (names, SQL type names,
    /// precision and scale). Waits for the statement to complete. Returns an empty list when the
    /// statement produced no result set or the backend did not report a schema.
    /// </summary>
    Task<IReadOnlyList<ColumnInfo>> GetSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the current execution status without blocking.</summary>
    Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a cancellation request for the in-flight operation.</summary>
    Task CancelAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases the operation handle. Idempotent.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
