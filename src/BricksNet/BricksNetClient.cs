using System.Diagnostics;
using BricksNet.Auth;
using BricksNet.Backends;
using BricksNet.Connection;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using BricksNet.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BricksNet;

/// <summary>
/// Fluent entry point for connecting to a Databricks workspace. Selects the REST or Thrift backend
/// based on <see cref="BricksNetClientOptions.UseThrift"/> and owns the lifetime of the underlying
/// transport and all opened sessions.
/// Mirrors <c>DBSQLClient</c> from <c>databricks-sql-nodejs/lib/DBSQLClient.ts</c>.
/// </summary>
public sealed class BricksNetClient : IBricksNetClient
{
    private readonly BricksNetClientOptions _options;
    private readonly BricksNetHttpClient _httpClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BricksNetClient> _logger;
    private readonly string _backendName;
    private readonly object _sessionsLock = new();
    private readonly List<BricksNetSession> _sessions = new();

    private readonly ServiceProvider? _ownedProvider;
    private readonly HttpClient? _ownedTokenExchangeClient;

    private IBackend? _backend;
    private bool _disposed;

    // RS0026: each public constructor has a distinct required second parameter (token, OAuth options,
    // auth provider), so the shared optional logger factory cannot make calls ambiguous.
#pragma warning disable RS0026
    /// <summary>
    /// Creates a self-contained client authenticating with a personal access token, or, when
    /// <see cref="BricksNetClientOptions.AuthType"/> is <see cref="AuthType.OAuthClientCredentials"/>,
    /// with a service principal's OAuth client secret. An internal service provider hosts the HTTP
    /// pipeline and is disposed with the client.
    /// </summary>
    /// <param name="options">Connection options.</param>
    /// <param name="accessToken">
    /// The personal access token for <see cref="AuthType.Pat"/>, or the client secret for
    /// <see cref="AuthType.OAuthClientCredentials"/> (with <see cref="BricksNetClientOptions.OAuthClientId"/>).
    /// </param>
    /// <param name="loggerFactory">Optional logger factory; lifecycle logs are suppressed when omitted.</param>
    public BricksNetClient(
        BricksNetClientOptions options,
        string? accessToken = null,
        ILoggerFactory? loggerFactory = null)
        : this(options, loggerFactory)
    {
        if (_options.AuthType == AuthType.OAuthClientCredentials)
        {
            // The token-exchange client must bypass the auth-injecting pipeline.
            _ownedTokenExchangeClient = CreateTokenExchangeClient();
        }

        IAuthProvider authProvider = AuthProviderFactory.Create(
            _options,
            accessToken: accessToken,
            tokenExchangeClient: _ownedTokenExchangeClient,
            loggerFactory: _loggerFactory);

        (_ownedProvider, _httpClient) = BuildOwnedPipeline(authProvider);
    }

    /// <summary>
    /// Creates a self-contained client authenticating with OAuth2 authorization-code + PKCE. An
    /// internal service provider hosts the HTTP pipeline and is disposed with the client.
    /// </summary>
    /// <param name="options">Connection options (set <see cref="BricksNetClientOptions.AuthType"/> to <see cref="AuthType.OAuthPkce"/>).</param>
    /// <param name="oauthOptions">OAuth client configuration.</param>
    /// <param name="loggerFactory">Optional logger factory; lifecycle logs are suppressed when omitted.</param>
    public BricksNetClient(
        BricksNetClientOptions options,
        OAuthOptions oauthOptions,
        ILoggerFactory? loggerFactory = null)
        : this(options, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(oauthOptions);

        // The token-exchange client must bypass the auth-injecting pipeline.
        _ownedTokenExchangeClient = CreateTokenExchangeClient();
        IAuthProvider authProvider = AuthProviderFactory.Create(
            _options,
            oauthOptions: oauthOptions,
            tokenExchangeClient: _ownedTokenExchangeClient,
            loggerFactory: _loggerFactory);

        (_ownedProvider, _httpClient) = BuildOwnedPipeline(authProvider);
    }

    /// <summary>
    /// Creates a self-contained client that gets its bearer tokens from
    /// <paramref name="authProvider"/>. Use this for credentials BricksNet does not obtain itself,
    /// such as Microsoft Entra ID tokens for Azure Databricks. An internal service provider hosts
    /// the HTTP pipeline and is disposed with the client.
    /// </summary>
    /// <param name="options">Connection options. <see cref="BricksNetClientOptions.AuthType"/> is ignored.</param>
    /// <param name="authProvider">
    /// Supplies the bearer token. <see cref="IAuthProvider.GetTokenAsync"/> is called for every HTTP
    /// request, so the provider should cache tokens and refresh them before they expire.
    /// </param>
    /// <param name="loggerFactory">Optional logger factory; lifecycle logs are suppressed when omitted.</param>
    public BricksNetClient(
        BricksNetClientOptions options,
        IAuthProvider authProvider,
        ILoggerFactory? loggerFactory = null)
        : this(options, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(authProvider);

        (_ownedProvider, _httpClient) = BuildOwnedPipeline(authProvider);
    }

#pragma warning restore RS0026

    /// <summary>
    /// Creates a client over a caller-supplied, already-configured HTTP pipeline client (as
    /// registered by <c>IServiceCollection.AddBricksNet</c>). The caller owns the pipeline lifetime.
    /// </summary>
    /// <param name="options">Connection options.</param>
    /// <param name="httpClient">The configured BricksNet HTTP pipeline client.</param>
    /// <param name="loggerFactory">Optional logger factory; lifecycle logs are suppressed when omitted.</param>
    internal BricksNetClient(
        BricksNetClientOptions options,
        BricksNetHttpClient httpClient,
        ILoggerFactory? loggerFactory = null)
        : this(options, loggerFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    private BricksNetClient(BricksNetClientOptions options, ILoggerFactory? loggerFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<BricksNetClient>();
        _backendName = BackendFactory.BackendName(_options);

        // Assigned by the delegating constructors. The field is non-null after construction.
        _httpClient = null!;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_backend is not null)
        {
            return;
        }

        LogMessages.ConnectionOpening(_logger, _options.Host, _options.Port, _backendName);

        using Activity? activity = BricksNetActivitySource.StartConnectionOpen(
            _options.Host, _options.Port, _backendName);

        var backend = BackendFactory.Create(_options, _httpClient);
        await backend.ConnectAsync(_options, cancellationToken).ConfigureAwait(false);
        _backend = backend;

        LogMessages.ConnectionOpened(_logger, _options.Host, _options.Port);
    }

    /// <inheritdoc />
    public async Task<IBricksNetSession> OpenSessionAsync(
        SessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_backend is null)
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        var sessionDefaults = options ?? new SessionOptions();
        var backendSessionOptions = new BackendSessionOptions
        {
            Catalog = sessionDefaults.Catalog,
            Schema = sessionDefaults.Schema,
            QueryTimeoutSeconds = sessionDefaults.QueryTimeoutSeconds,
            MaxRows = sessionDefaults.MaxRows,
            QueryTags = sessionDefaults.QueryTags,
        };

        var sessionBackend = await _backend!
            .OpenSessionAsync(backendSessionOptions, cancellationToken)
            .ConfigureAwait(false);

        var session = new BricksNetSession(
            sessionBackend,
            sessionDefaults,
            _options,
            _backendName,
            _loggerFactory,
            RemoveSession);

        lock (_sessionsLock)
        {
            _sessions.Add(session);
        }

        LogMessages.SessionOpened(_logger, session.Id);
        return session;
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _backend is null)
        {
            return;
        }

        using Activity? activity = BricksNetActivitySource.StartConnectionClose(
            _options.Host, _options.Port, _backendName);

        BricksNetSession[] sessions;
        lock (_sessionsLock)
        {
            sessions = _sessions.ToArray();
            _sessions.Clear();
        }

        foreach (var session in sessions)
        {
            await session.CloseAsync(cancellationToken).ConfigureAwait(false);
        }

        await _backend.CloseAsync(cancellationToken).ConfigureAwait(false);
        _backend = null;

        LogMessages.ConnectionClosed(_logger);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            _ownedTokenExchangeClient?.Dispose();
            _ownedProvider?.Dispose();
        }
    }

    /// <summary>
    /// Creates the client for OAuth token requests. It shares the pipeline's proxy and TLS settings
    /// but not its handlers, so no bearer token is attached to the token request itself.
    /// </summary>
    private HttpClient CreateTokenExchangeClient() =>
        new(Connection.HttpClientBuilderExtensions.CreatePrimaryHandler(_options, _logger), disposeHandler: true);

    private (ServiceProvider Provider, BricksNetHttpClient HttpClient) BuildOwnedPipeline(
        IAuthProvider authProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();   // ensures ILogger<T> resolves inside the owned provider
        if (_loggerFactory is not NullLoggerFactory)
        {
            services.AddSingleton(_loggerFactory);
        }

        services.AddSingleton(authProvider);
        services.AddBricksNetHttpClient(_options);

        var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<BricksNetHttpClient>();
        return (provider, httpClient);
    }

    private void RemoveSession(BricksNetSession session)
    {
        lock (_sessionsLock)
        {
            _sessions.Remove(session);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BricksNetClient));
        }
    }
}
