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

    /// <summary>Returns the current execution status without blocking.</summary>
    Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a cancellation request for the in-flight operation.</summary>
    Task CancelAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases the operation handle. Idempotent.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
