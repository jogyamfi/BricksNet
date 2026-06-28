using BricksNet.Connection;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Backends.Thrift.Generated;
using Thrift;
using Thrift.Protocol;

namespace BricksNet.Backends.Thrift;

/// <summary>
/// Thrift-over-HTTP implementation of <see cref="IBackend"/>. Owns the transport,
/// binary protocol, and TCLIService client for the lifetime of the connection.
/// </summary>
internal sealed class ThriftBackend : IBackend, IAsyncDisposable
{
    private readonly BricksNetHttpClient _httpClient;

    private ThriftHttpTransport? _transport;
    private TProtocol? _protocol;
    private TCLIServiceClient? _client;

    public ThriftBackend(BricksNetHttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public Task ConnectAsync(BricksNetClientOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var endpoint = new UriBuilder("https", options.Host, options.Port, options.HttpPath).Uri.ToString();
        var configuration = new TConfiguration { MaxMessageSize = int.MaxValue };

        _transport = new ThriftHttpTransport(_httpClient, endpoint, configuration);
        _protocol = new TBinaryProtocol(_transport);
        _client = new TCLIServiceClient(_protocol);

        return Task.CompletedTask;
    }

    public async Task<ISessionBackend> OpenSessionAsync(
        BackendSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var client = _client ?? throw new InvalidOperationException(
            "ConnectAsync must be called before OpenSessionAsync.");

        TNamespace? initialNamespace = options.Catalog is not null || options.Schema is not null
            ? new TNamespace { CatalogName = options.Catalog, SchemaName = options.Schema }
            : null;

        var request = new TOpenSessionReq
        {
            Configuration = options.QueryTags,
            InitialNamespace = initialNamespace,
            CanUseMultipleCatalogs = true,
        };

        var response = await client.OpenSessionAsync(request, cancellationToken).ConfigureAwait(false);
        ThriftStatusChecker.EnsureSuccess(response.Status);

        if (response.SessionHandle is null)
        {
            throw new InvalidOperationException("OpenSession succeeded but returned no session handle.");
        }

        return new ThriftSessionBackend(client, response.SessionHandle, options);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _transport?.Dispose();
        _transport = null;
        _protocol = null;
        _client = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _transport?.Dispose();
        _transport = null;
        _protocol = null;
        _client = null;
        return ValueTask.CompletedTask;
    }
}
