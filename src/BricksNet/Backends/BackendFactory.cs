using BricksNet.Backends.Rest;
using BricksNet.Backends.Thrift;
using BricksNet.Connection;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;

namespace BricksNet.Backends;

/// <summary>
/// Selects and constructs the backend implementation for a connection based on
/// <see cref="BricksNetClientOptions.UseThrift"/>.
/// </summary>
internal static class BackendFactory
{
    /// <summary>
    /// Creates a <see cref="ThriftBackend"/> when <see cref="BricksNetClientOptions.UseThrift"/> is
    /// <see langword="true"/>; otherwise a <see cref="RestBackend"/>. Both share the supplied
    /// auth-injecting HTTP pipeline client.
    /// </summary>
    /// <param name="options">Client options selecting the backend protocol.</param>
    /// <param name="httpClient">The configured BricksNet HTTP pipeline client.</param>
    /// <returns>The constructed <see cref="IBackend"/>.</returns>
    public static IBackend Create(BricksNetClientOptions options, BricksNetHttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        return options.UseThrift
            ? new ThriftBackend(httpClient)
            : new RestBackend(httpClient);
    }

    /// <summary>The canonical backend name used in telemetry and logs.</summary>
    public static string BackendName(BricksNetClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseThrift ? "thrift" : "rest";
    }
}
