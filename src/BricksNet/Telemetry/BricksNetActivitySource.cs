using System.Diagnostics;
using System.Reflection;

namespace BricksNet.Telemetry;

/// <summary>
/// The <see cref="ActivitySource"/> BricksNet emits OpenTelemetry-compatible spans on, for
/// connection, statement, result-fetch, and auth operations. Subscribe with
/// <c>tracing.AddSource(BricksNetActivitySource.SourceName)</c>.
/// </summary>
/// <remarks>
/// Span attributes follow the OpenTelemetry database semantic conventions
/// (<see href="https://opentelemetry.io/docs/specs/semconv/database/database-spans/"/>). The query
/// text (the <c>db.statement</c> attribute) is attached only when sensitive-data logging is
/// explicitly enabled, and is truncated to 1,000 characters. No token or
/// credential value is ever recorded.
/// </remarks>
public static class BricksNetActivitySource
{
    /// <summary>Name of the <see cref="ActivitySource"/>; subscribe to this with an <c>ActivityListener</c>.</summary>
    public const string SourceName = "BricksNet";

    /// <summary>Maximum number of characters of SQL text recorded on a span.</summary>
    internal const int MaxStatementLength = 1000;

    private static readonly string SourceVersion = ResolveVersion();

    /// <summary>The shared <see cref="ActivitySource"/> instance.</summary>
    internal static ActivitySource Instance { get; } = new(SourceName, SourceVersion);

    /// <summary>Starts a span covering opening a connection / session to the workspace.</summary>
    internal static Activity? StartConnectionOpen(string serverAddress, int serverPort, string backend)
    {
        Activity? activity = Instance.StartActivity("databricks.connection.open", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        SetCommonAttributes(activity, serverAddress, serverPort, backend);
        return activity;
    }

    /// <summary>Starts a span covering closing a connection / session.</summary>
    internal static Activity? StartConnectionClose(string serverAddress, int serverPort, string backend)
    {
        Activity? activity = Instance.StartActivity("databricks.connection.close", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        SetCommonAttributes(activity, serverAddress, serverPort, backend);
        return activity;
    }

    /// <summary>
    /// Starts a span covering a statement execution. The <paramref name="sql"/> text is attached only
    /// when <paramref name="sensitiveDataLoggingEnabled"/> is <see langword="true"/>.
    /// </summary>
    internal static Activity? StartStatementExecute(
        string sql,
        string? catalog,
        string? schema,
        string serverAddress,
        int serverPort,
        string backend,
        bool sensitiveDataLoggingEnabled)
    {
        Activity? activity = Instance.StartActivity("databricks.statement.execute", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        SetCommonAttributes(activity, serverAddress, serverPort, backend);

        string? databaseName = BuildDatabaseName(catalog, schema);
        if (databaseName is not null)
        {
            activity.SetTag(AttributeNames.DbName, databaseName);
        }

        if (sensitiveDataLoggingEnabled && !string.IsNullOrEmpty(sql))
        {
            activity.SetTag(AttributeNames.DbStatement, Truncate(sql, MaxStatementLength));
        }

        return activity;
    }

    /// <summary>Starts a span covering fetching a chunk of results for a statement.</summary>
    internal static Activity? StartResultFetch(string? statementId, string backend)
    {
        Activity? activity = Instance.StartActivity("databricks.result.fetch", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(AttributeNames.DbSystem, DbSystemValue);
        activity.SetTag(AttributeNames.DatabricksBackend, backend);
        SetStatementId(activity, statementId);
        return activity;
    }

    /// <summary>Starts a span covering acquiring an authentication token. The token is never recorded.</summary>
    internal static Activity? StartAuthTokenFetch(string authType)
    {
        Activity? activity = Instance.StartActivity("databricks.auth.token", ActivityKind.Client);
        activity?.SetTag(AttributeNames.DatabricksAuthType, authType);
        return activity;
    }

    /// <summary>Records the server-assigned statement identifier on an in-progress span.</summary>
    internal static void SetStatementId(Activity? activity, string? statementId)
    {
        if (activity is not null && !string.IsNullOrEmpty(statementId))
        {
            activity.SetTag(AttributeNames.DatabricksStatementId, statementId);
        }
    }

    private static void SetCommonAttributes(Activity activity, string serverAddress, int serverPort, string backend)
    {
        activity.SetTag(AttributeNames.DbSystem, DbSystemValue);
        activity.SetTag(AttributeNames.ServerAddress, serverAddress);
        activity.SetTag(AttributeNames.ServerPort, serverPort);
        activity.SetTag(AttributeNames.DatabricksBackend, backend);
    }

    private static string? BuildDatabaseName(string? catalog, string? schema)
    {
        bool hasCatalog = !string.IsNullOrEmpty(catalog);
        bool hasSchema = !string.IsNullOrEmpty(schema);
        return (hasCatalog, hasSchema) switch
        {
            (true, true) => $"{catalog}.{schema}",
            (true, false) => catalog,
            (false, true) => schema,
            _ => null,
        };
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string ResolveVersion()
    {
        string? informational = typeof(BricksNetActivitySource).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string version = informational ?? typeof(BricksNetActivitySource).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        int plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    /// <summary><c>db.system</c> value identifying the database technology.</summary>
    private const string DbSystemValue = "databricks";

    /// <summary>Canonical OpenTelemetry / BricksNet span attribute names.</summary>
    internal static class AttributeNames
    {
        /// <summary><c>db.system</c> — the database management system.</summary>
        public const string DbSystem = "db.system";

        /// <summary><c>db.statement</c> — the executed SQL text (sensitive; truncated).</summary>
        public const string DbStatement = "db.statement";

        /// <summary><c>db.name</c> — the logical database, formatted as <c>catalog.schema</c>.</summary>
        public const string DbName = "db.name";

        /// <summary><c>server.address</c> — the workspace host.</summary>
        public const string ServerAddress = "server.address";

        /// <summary><c>server.port</c> — the workspace port.</summary>
        public const string ServerPort = "server.port";

        /// <summary><c>databricks.statement_id</c> — the server-assigned statement identifier.</summary>
        public const string DatabricksStatementId = "databricks.statement_id";

        /// <summary><c>databricks.backend</c> — the active backend (<c>rest</c> or <c>thrift</c>).</summary>
        public const string DatabricksBackend = "databricks.backend";

        /// <summary><c>databricks.auth_type</c> — the authentication mechanism in use.</summary>
        public const string DatabricksAuthType = "databricks.auth_type";
    }
}
