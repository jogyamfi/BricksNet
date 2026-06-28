using BricksNet.Connection;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;

namespace BricksNet.Backends.Rest;

/// <summary>
/// REST implementation of <see cref="IBackend"/> over the Databricks SQL Statement Execution API.
/// The API is stateless, so this backend holds only the warehouse id (parsed from
/// <see cref="BricksNetClientOptions.HttpPath"/>) and the shared HTTP client; sessions are
/// synthesised by <see cref="RestSessionBackend"/>.
/// </summary>
internal sealed class RestBackend : IBackend
{
    private readonly BricksNetHttpClient _httpClient;

    private SqlExecHttpClient? _client;
    private RestStatementPoller? _poller;
    private string? _warehouseId;

    public RestBackend(BricksNetHttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public Task ConnectAsync(BricksNetClientOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        _warehouseId = ExtractWarehouseId(options.HttpPath);
        _client = new SqlExecHttpClient(_httpClient);
        _poller = new RestStatementPoller(_client);

        return Task.CompletedTask;
    }

    public Task<ISessionBackend> OpenSessionAsync(
        BackendSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (_client is null || _poller is null || _warehouseId is null)
        {
            throw new InvalidOperationException("ConnectAsync must be called before OpenSessionAsync.");
        }

        ISessionBackend session = new RestSessionBackend(_client, _poller, _warehouseId, options);
        return Task.FromResult(session);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client = null;
        _poller = null;
        _warehouseId = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Extracts the warehouse id from an HTTP path such as
    /// <c>/sql/1.0/warehouses/{id}</c> or <c>/sql/1.0/endpoints/{id}</c>. Falls back to the
    /// final path segment when no marker is present.
    /// </summary>
    private static string ExtractWarehouseId(string httpPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(httpPath);

        var segments = httpPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException(
                "HttpPath does not contain a warehouse id.", nameof(httpPath));
        }

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("warehouses", StringComparison.OrdinalIgnoreCase) ||
                segments[i].Equals("endpoints", StringComparison.OrdinalIgnoreCase))
            {
                return segments[i + 1];
            }
        }

        return segments[^1];
    }
}
