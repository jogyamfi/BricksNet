using BricksNet.Contracts.Options;
using BricksNet.Results;

namespace BricksNet.Contracts;

/// <summary>
/// Backend handle scoped to a single operation. Returned by the statement-execution
/// and metadata methods on <see cref="ISessionBackend"/>.
/// Mirrors <c>IOperationBackend</c> from <c>databricks-sql-nodejs/lib/contracts/IOperationBackend.ts</c>.
/// </summary>
internal interface IOperationBackend
{
    /// <summary>Opaque operation identifier assigned by the server.</summary>
    string Id { get; }

    /// <summary>Polls the server for the current execution status.</summary>
    Task<QueryStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the next page of results according to <paramref name="options"/>. Call repeatedly
    /// while the returned <see cref="FetchResult.HasMoreRows"/> is <see langword="true"/>.
    /// </summary>
    Task<FetchResult> FetchResultsAsync(FetchOptions options);

    /// <summary>Requests cancellation of the in-flight operation.</summary>
    Task CancelAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases the operation handle on the server. Idempotent.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
