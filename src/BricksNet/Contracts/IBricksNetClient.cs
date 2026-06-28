using BricksNet.Contracts.Options;

namespace BricksNet.Contracts;

/// <summary>
/// Fluent entry point for connecting to a Databricks workspace.
/// Mirrors <c>DBSQLClient</c> from <c>databricks-sql-nodejs/lib/DBSQLClient.ts</c>.
/// </summary>
public interface IBricksNetClient : IAsyncDisposable
{
    /// <summary>Establishes the underlying transport connection.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a new session with the given defaults.</summary>
    Task<IBricksNetSession> OpenSessionAsync(
        SessionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the client and all associated sessions. Idempotent.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
