using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;

namespace BricksNet.AdoNet;

/// <summary>
/// An ADO.NET connection to a Databricks SQL warehouse or cluster, wrapping the
/// fluent <see cref="IBricksNetClient"/> / <see cref="IBricksNetSession"/> API.
/// </summary>
/// <remarks>
/// Databricks SQL has no multi-statement transactions, so <see cref="BeginDbTransaction"/>
/// is unsupported. Prefer the asynchronous <see cref="OpenAsync(CancellationToken)"/> /
/// <see cref="CloseAsync"/> methods; the synchronous variants block on them and can deadlock
/// when invoked from a single-threaded synchronization context.
/// </remarks>
public sealed class BricksNetConnection : DbConnection
{
    private readonly Func<BricksNetClientOptions, string?, IBricksNetClient>? _clientFactory;

    private string _connectionString = string.Empty;
    private BricksNetClientOptions? _clientOptions;
    private string? _accessToken;
    private SessionOptions _sessionOptions = new();

    private IBricksNetClient? _client;
    private IBricksNetSession? _session;
    private string? _serverVersion;
    private long _keptSince;
    private ConnectionState _state = ConnectionState.Closed;

    /// <summary>Initialises an empty connection.</summary>
    public BricksNetConnection()
    {
    }

    /// <summary>Initialises a connection from a connection string.</summary>
    /// <param name="connectionString">The BricksNet connection string.</param>
    public BricksNetConnection(string connectionString)
    {
        ConnectionString = connectionString;
    }

    /// <summary>
    /// Initialises a connection from a connection string, creating its fluent client with
    /// <paramref name="clientFactory"/> instead of the static <see cref="ClientFactory"/>.
    /// </summary>
    /// <param name="connectionString">The BricksNet connection string.</param>
    /// <param name="clientFactory">
    /// Creates the client from the parsed options and access token; <see langword="null"/> uses
    /// <see cref="ClientFactory"/>.
    /// </param>
    public BricksNetConnection(
        string connectionString,
        Func<BricksNetClientOptions, string?, IBricksNetClient>? clientFactory)
    {
        _clientFactory = clientFactory;
        ConnectionString = connectionString;
    }

    /// <summary>Initialises a connection from pre-built client options.</summary>
    /// <param name="options">The client options.</param>
    /// <param name="accessToken">Optional access token used for PAT authentication.</param>
    /// <param name="sessionOptions">Optional per-session defaults.</param>
    /// <param name="clientFactory">
    /// Optional factory for the fluent client; <see langword="null"/> uses <see cref="ClientFactory"/>.
    /// </param>
    public BricksNetConnection(
        BricksNetClientOptions options,
        string? accessToken = null,
        SessionOptions? sessionOptions = null,
        Func<BricksNetClientOptions, string?, IBricksNetClient>? clientFactory = null)
    {
        _clientOptions = options ?? throw new ArgumentNullException(nameof(options));
        _accessToken = accessToken;
        _sessionOptions = sessionOptions ?? new SessionOptions();
        _clientFactory = clientFactory;
    }

    /// <summary>
    /// Factory that creates the underlying fluent client from the parsed options and access
    /// token, used by connections constructed without their own factory. Defaults to the
    /// production <see cref="BricksNetClient"/>. Tests may override it to inject a fake client, but
    /// the constructor overloads that take a factory are preferred because they are safe in parallel.
    /// </summary>
    public static Func<BricksNetClientOptions, string?, IBricksNetClient>? ClientFactory { get; set; }
        = static (options, accessToken) => new BricksNetClient(options, accessToken);

    /// <inheritdoc />
    [AllowNull]
    public override string ConnectionString
    {
        get => _connectionString;
        set
        {
            if (_state != ConnectionState.Closed)
            {
                throw new InvalidOperationException("The connection string cannot be changed while the connection is open.");
            }

            // A session kept alive by Close() belongs to the previous target; release it first.
            if (_client is not null)
            {
                CleanupAsync().GetAwaiter().GetResult();
            }

            _connectionString = value ?? string.Empty;
            var builder = new BricksNetConnectionStringBuilder(_connectionString);
            _clientOptions = builder.ToClientOptions(out _accessToken);
            _sessionOptions = builder.ToSessionOptions();
            KeepSessionAlive = builder.KeepSessionAlive;
            KeptSessionIdleTimeout = TimeSpan.FromSeconds(builder.KeptSessionIdleTimeoutSeconds);
        }
    }

    /// <summary>The default for <see cref="KeptSessionIdleTimeout"/>: 15 minutes.</summary>
    public static readonly TimeSpan DefaultKeptSessionIdleTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// When <see langword="true"/>, <see cref="Close"/> keeps the Databricks session open so the
    /// next <see cref="Open"/> reuses it instead of starting a new one; the session is released when
    /// the connection is disposed. Set from the <c>KeepSessionAlive</c> connection-string keyword.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The server can expire an idle session, so a session kept for longer than
    /// <see cref="KeptSessionIdleTimeout"/> is replaced by a new one on the next open.
    /// </remarks>
    public bool KeepSessionAlive { get; set; }

    /// <summary>
    /// How long a session kept by <see cref="KeepSessionAlive"/> may sit idle before the next open
    /// replaces it with a new session, rather than risk using one the server has expired. Set from the
    /// <c>KeptSessionIdleTimeoutSeconds</c> connection-string keyword. Defaults to 15 minutes.
    /// </summary>
    public TimeSpan KeptSessionIdleTimeout { get; set; } = DefaultKeptSessionIdleTimeout;

    /// <summary>The clock used to measure how long a kept session has been idle; replaceable in tests.</summary>
    internal TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <inheritdoc />
    public override string Database => _sessionOptions.Schema ?? string.Empty;

    /// <inheritdoc />
    public override string DataSource => _clientOptions?.Host ?? string.Empty;

    /// <summary>
    /// The Apache Spark version reported by the server (for example <c>3.5.0</c>). While open, the
    /// first access runs a query and blocks on it, so prefer <see cref="GetServerVersionAsync"/>.
    /// While closed, returns the cached value or an empty string.
    /// </summary>
    public override string ServerVersion => _state == ConnectionState.Open
        ? GetServerVersionAsync(CancellationToken.None).GetAwaiter().GetResult()
        : _serverVersion ?? string.Empty;

    /// <summary>
    /// <see cref="ServerVersion"/> parsed as a <see cref="Version"/>, or <see langword="null"/>
    /// when it is unknown or cannot be parsed. Use it to gate features on the server version.
    /// </summary>
    public Version? SparkVersion => Version.TryParse(ServerVersion, out var version) ? version : null;

    /// <inheritdoc />
    public override ConnectionState State => _state;

    /// <summary>The per-session defaults applied to commands created from this connection.</summary>
    internal SessionOptions EffectiveSessionOptions => _sessionOptions;

    /// <inheritdoc />
    public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_state == ConnectionState.Open)
        {
            return;
        }

        if (_state != ConnectionState.Closed)
        {
            throw new InvalidOperationException($"The connection cannot be opened from state '{_state}'.");
        }

        if (_clientOptions is null)
        {
            throw new InvalidOperationException("No connection string or client options have been supplied.");
        }

        // A session kept alive by a previous Close() is reused, unless it has been idle long enough that
        // the server may have expired it.
        if (_client is not null && _keptSince != 0 && TimeProvider.GetElapsedTime(_keptSince) > KeptSessionIdleTimeout)
        {
            await ReleaseStaleSessionAsync().ConfigureAwait(false);
        }

        _keptSince = 0;

        var factory = _client is not null
            ? null
            : _clientFactory ?? ClientFactory
                ?? throw new InvalidOperationException(
                    "No BricksNet client factory is configured. Pass one to the constructor or assign "
                    + "BricksNetConnection.ClientFactory.");

        SetState(ConnectionState.Connecting);
        try
        {
            if (factory is not null)
            {
                // Assigned before connecting so that a failed connect is still cleaned up below.
                _client = factory(_clientOptions, _accessToken);
                await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            _session ??= await _client!.OpenSessionAsync(_sessionOptions, cancellationToken).ConfigureAwait(false);
            SetState(ConnectionState.Open);
        }
        catch
        {
            await CleanupAsync().ConfigureAwait(false);
            SetState(ConnectionState.Closed);
            throw;
        }
    }

    /// <inheritdoc />
    public override void Close() => CloseAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Closes the connection. With <see cref="KeepSessionAlive"/> set, the session stays open for
    /// the next <see cref="OpenAsync(CancellationToken)"/> to reuse.
    /// </summary>
    public override async Task CloseAsync()
    {
        if (_state == ConnectionState.Closed)
        {
            return;
        }

        if (KeepSessionAlive)
        {
            _keptSince = TimeProvider.GetTimestamp();
        }
        else
        {
            await CleanupAsync().ConfigureAwait(false);
        }

        SetState(ConnectionState.Closed);
    }

    // Closing a session the server already expired can fail; the connection is replacing it anyway.
    private async Task ReleaseStaleSessionAsync()
    {
        try
        {
            await CleanupAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Sets the default schema for unqualified object references. Takes effect on the next
    /// opened session: an open session is re-opened to apply the change, and a session kept alive
    /// while closed is released.
    /// </summary>
    /// <param name="databaseName">The schema name.</param>
    public override void ChangeDatabase(string databaseName)
    {
        ArgumentException.ThrowIfNullOrEmpty(databaseName);
        _sessionOptions = _sessionOptions with { Schema = databaseName };

        if (_client is null || _session is null)
        {
            return;
        }

        _session.CloseAsync().GetAwaiter().GetResult();
        _session = null;
        if (_state == ConnectionState.Open)
        {
            _session = _client.OpenSessionAsync(_sessionOptions).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Returns the Apache Spark version reported by the server (for example <c>3.5.0</c>). Runs
    /// <c>SELECT version()</c> on the first call and caches the result until the session is released.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="InvalidOperationException">The connection is not open and no version is cached.</exception>
    public async Task<string> GetServerVersionAsync(CancellationToken cancellationToken = default)
    {
        if (_serverVersion is not null)
        {
            return _serverVersion;
        }

        var operation = await GetOpenSession()
            .ExecuteStatementAsync("SELECT version()", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var rows = await operation
                .FetchChunkAsync<IReadOnlyDictionary<string, object?>>(1, cancellationToken)
                .ConfigureAwait(false);

            // version() returns "<spark version> <git revision>"; keep only the version number.
            var text = rows.Count > 0
                ? Convert.ToString(rows[0].Values.FirstOrDefault(), CultureInfo.InvariantCulture)
                : null;
            _serverVersion = text?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return _serverVersion;
        }
        finally
        {
            await operation.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override DbCommand CreateDbCommand() => new BricksNetCommand(string.Empty, this);

    /// <summary>
    /// Always throws: Databricks SQL has no multi-statement transactions.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException("Databricks SQL does not support multi-statement transactions.");

    /// <summary>Returns the open session for command execution.</summary>
    internal IBricksNetSession GetOpenSession()
    {
        if (_state != ConnectionState.Open || _session is null)
        {
            throw new InvalidOperationException("The connection is not open.");
        }

        return _session;
    }

    /// <summary>Closes the connection and releases its session, including one kept alive.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (_state != ConnectionState.Closed || _client is not null))
        {
            CleanupAsync().GetAwaiter().GetResult();
            SetState(ConnectionState.Closed);
        }

        base.Dispose(disposing);
    }

    /// <summary>Closes the connection and releases its session, including one kept alive.</summary>
    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync().ConfigureAwait(false);
        SetState(ConnectionState.Closed);
        GC.SuppressFinalize(this);
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (_session is not null)
            {
                await _session.CloseAsync().ConfigureAwait(false);
            }

            if (_client is not null)
            {
                await _client.CloseAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _session = null;
            _client = null;
            _serverVersion = null;
        }
    }

    private void SetState(ConnectionState newState)
    {
        if (_state == newState)
        {
            return;
        }

        var previous = _state;
        _state = newState;
        OnStateChange(new StateChangeEventArgs(previous, newState));
    }
}
