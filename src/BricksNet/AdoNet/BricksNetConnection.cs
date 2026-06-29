using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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
    private string _connectionString = string.Empty;
    private BricksNetClientOptions? _clientOptions;
    private string? _accessToken;
    private SessionOptions _sessionOptions = new();

    private IBricksNetClient? _client;
    private IBricksNetSession? _session;
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

    /// <summary>Initialises a connection from pre-built client options.</summary>
    /// <param name="options">The client options.</param>
    /// <param name="accessToken">Optional access token used for PAT authentication.</param>
    /// <param name="sessionOptions">Optional per-session defaults.</param>
    public BricksNetConnection(
        BricksNetClientOptions options,
        string? accessToken = null,
        SessionOptions? sessionOptions = null)
    {
        _clientOptions = options ?? throw new ArgumentNullException(nameof(options));
        _accessToken = accessToken;
        _sessionOptions = sessionOptions ?? new SessionOptions();
    }

    /// <summary>
    /// Factory that creates the underlying fluent client from the parsed options and access
    /// token. The Phase 10 fluent API wires the production implementation here; tests may
    /// override it to inject a fake client. Until a factory is assigned, opening a connection
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static Func<BricksNetClientOptions, string?, IBricksNetClient>? ClientFactory { get; set; }

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

            _connectionString = value ?? string.Empty;
            var builder = new BricksNetConnectionStringBuilder(_connectionString);
            _clientOptions = builder.ToClientOptions(out _accessToken);
            _sessionOptions = builder.ToSessionOptions();
        }
    }

    /// <inheritdoc />
    public override string Database => _sessionOptions.Schema ?? string.Empty;

    /// <inheritdoc />
    public override string DataSource => _clientOptions?.Host ?? string.Empty;

    /// <summary>Returns the Databricks server version, when known; otherwise an empty string.</summary>
    public override string ServerVersion => string.Empty;

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

        var factory = ClientFactory
            ?? throw new InvalidOperationException(
                "No BricksNet client factory is configured. Assign BricksNetConnection.ClientFactory "
                + "(the Phase 10 fluent API registers the production factory).");

        SetState(ConnectionState.Connecting);
        try
        {
            var client = factory(_clientOptions, _accessToken);
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var session = await client.OpenSessionAsync(_sessionOptions, cancellationToken).ConfigureAwait(false);

            _client = client;
            _session = session;
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

    /// <inheritdoc />
    public override async Task CloseAsync()
    {
        if (_state == ConnectionState.Closed)
        {
            return;
        }

        await CleanupAsync().ConfigureAwait(false);
        SetState(ConnectionState.Closed);
    }

    /// <summary>
    /// Sets the default schema for unqualified object references. Takes effect on the next
    /// opened session; an already-open session is re-opened to apply the change.
    /// </summary>
    /// <param name="databaseName">The schema name.</param>
    public override void ChangeDatabase(string databaseName)
    {
        ArgumentException.ThrowIfNullOrEmpty(databaseName);
        _sessionOptions = _sessionOptions with { Schema = databaseName };

        if (_state == ConnectionState.Open && _client is not null)
        {
            _session?.CloseAsync().GetAwaiter().GetResult();
            _session = _client.OpenSessionAsync(_sessionOptions).GetAwaiter().GetResult();
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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _state != ConnectionState.Closed)
        {
            CleanupAsync().GetAwaiter().GetResult();
            SetState(ConnectionState.Closed);
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
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
