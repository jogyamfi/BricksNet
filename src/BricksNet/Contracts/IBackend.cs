using BricksNet.Contracts.Options;

namespace BricksNet.Contracts;

/// <summary>
/// Top-level backend dispatch handle. One instance per <c>BricksNetClient</c>,
/// selected at connect time based on <see cref="BricksNetClientOptions.UseThrift"/>.
/// Mirrors <c>IBackend</c> from <c>databricks-sql-nodejs/lib/contracts/IBackend.ts</c>.
/// </summary>
internal interface IBackend
{
    /// <summary>Establishes backend-level state using the supplied connection options.</summary>
    Task ConnectAsync(BricksNetClientOptions options, CancellationToken cancellationToken = default);

    /// <summary>Opens a session. The returned <see cref="ISessionBackend"/> is owned by the caller.</summary>
    Task<ISessionBackend> OpenSessionAsync(BackendSessionOptions options, CancellationToken cancellationToken = default);

    /// <summary>Tears down backend-level resources. Safe to call on a partially-initialised backend.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
