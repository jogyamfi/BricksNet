using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using BricksNet.Contracts.Options;

namespace BricksNet.AdoNet;

/// <summary>
/// Strongly-typed builder for a BricksNet ADO.NET connection string.
/// Exposes every <see cref="BricksNetClientOptions"/> setting (plus the access token,
/// catalog/schema defaults and proxy/SSL knobs) as a typed property that round-trips
/// through the underlying <see cref="DbConnectionStringBuilder"/> keyword dictionary.
/// </summary>
/// <remarks>
/// Several keywords accept aliases for familiarity with other providers, e.g.
/// <c>Host</c> / <c>Server</c> / <c>Data Source</c> all map to <see cref="Host"/>.
/// </remarks>
public sealed class BricksNetConnectionStringBuilder : DbConnectionStringBuilder
{
    private const string HostKey = "Host";
    private const string PortKey = "Port";
    private const string HttpPathKey = "HttpPath";
    private const string TokenKey = "Token";
    private const string AuthTypeKey = "AuthType";
    private const string UseThriftKey = "UseThrift";
    private const string CatalogKey = "Catalog";
    private const string SchemaKey = "Schema";
    private const string QueryTimeoutKey = "QueryTimeoutSeconds";
    private const string MaxRowsKey = "MaxRows";
    private const string ProxyUriKey = "ProxyUri";
    private const string ProxyTypeKey = "ProxyType";
    private const string ProxyUserKey = "ProxyUser";
    private const string ProxyPasswordKey = "ProxyPassword";
    private const string AllowSelfSignedKey = "AllowSelfSignedCerts";
    private const string UserAgentKey = "UserAgent";

    /// <summary>Maps each alias keyword to its canonical keyword.</summary>
    private static readonly IReadOnlyDictionary<string, string> KeywordAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Server"] = HostKey,
            ["Data Source"] = HostKey,
            ["DataSource"] = HostKey,
            ["Hostname"] = HostKey,
            ["HTTP Path"] = HttpPathKey,
            ["Http_Path"] = HttpPathKey,
            ["PWD"] = TokenKey,
            ["Password"] = TokenKey,
            ["Access Token"] = TokenKey,
            ["AccessToken"] = TokenKey,
            ["PersonalAccessToken"] = TokenKey,
            ["Default Catalog"] = CatalogKey,
            ["Initial Catalog"] = CatalogKey,
            ["Default Schema"] = SchemaKey,
            ["Database"] = SchemaKey,
        };

    /// <summary>Initialises an empty builder.</summary>
    public BricksNetConnectionStringBuilder()
    {
    }

    /// <summary>Initialises a builder from an existing connection string.</summary>
    /// <param name="connectionString">The connection string to parse.</param>
    public BricksNetConnectionStringBuilder(string connectionString)
    {
        ConnectionString = connectionString;
    }

    /// <summary>Databricks workspace hostname (e.g. <c>my-workspace.azuredatabricks.net</c>).</summary>
    public string? Host
    {
        get => GetString(HostKey);
        set => this[HostKey] = value!;
    }

    /// <summary>HTTPS port. Defaults to <c>443</c>.</summary>
    public int Port
    {
        get => GetInt32(PortKey, 443);
        set => this[PortKey] = value;
    }

    /// <summary>HTTP path to the SQL warehouse or cluster (e.g. <c>/sql/1.0/warehouses/&lt;id&gt;</c>).</summary>
    public string? HttpPath
    {
        get => GetString(HttpPathKey);
        set => this[HttpPathKey] = value!;
    }

    /// <summary>
    /// Personal access token or OAuth bearer token. The value is never echoed in logs;
    /// diagnostics referencing this builder must substitute <c>"***"</c>.
    /// </summary>
    public string? Token
    {
        get => GetString(TokenKey);
        set => this[TokenKey] = value!;
    }

    /// <summary>Authentication mechanism. Defaults to <see cref="AuthType.Pat"/>.</summary>
    public AuthType AuthType
    {
        get => GetEnum(AuthTypeKey, AuthType.Pat);
        set => this[AuthTypeKey] = value.ToString();
    }

    /// <summary>When <see langword="true"/>, connects via the Thrift backend; otherwise REST. Defaults to <see langword="false"/> (REST).</summary>
    public bool UseThrift
    {
        get => GetBoolean(UseThriftKey, false);
        set => this[UseThriftKey] = value;
    }

    /// <summary>Default catalog for unqualified object references.</summary>
    public string? Catalog
    {
        get => GetString(CatalogKey);
        set => this[CatalogKey] = value!;
    }

    /// <summary>Default schema for unqualified object references.</summary>
    public string? Schema
    {
        get => GetString(SchemaKey);
        set => this[SchemaKey] = value!;
    }

    /// <summary>Statement timeout in seconds. <c>0</c> means no timeout.</summary>
    public int QueryTimeoutSeconds
    {
        get => GetInt32(QueryTimeoutKey, 0);
        set => this[QueryTimeoutKey] = value;
    }

    /// <summary>Maximum rows per fetch chunk. Defaults to <c>10000</c>.</summary>
    public int MaxRows
    {
        get => GetInt32(MaxRowsKey, 10_000);
        set => this[MaxRowsKey] = value;
    }

    /// <summary>Optional proxy server address (e.g. <c>http://proxy.corp:8080</c>).</summary>
    public string? ProxyUri
    {
        get => GetString(ProxyUriKey);
        set => this[ProxyUriKey] = value!;
    }

    /// <summary>Proxy transport protocol. Defaults to <see cref="BricksNet.Contracts.Options.ProxyType.Http"/>.</summary>
    public ProxyType ProxyType
    {
        get => GetEnum(ProxyTypeKey, ProxyType.Http);
        set => this[ProxyTypeKey] = value.ToString();
    }

    /// <summary>Optional proxy username.</summary>
    public string? ProxyUser
    {
        get => GetString(ProxyUserKey);
        set => this[ProxyUserKey] = value!;
    }

    /// <summary>Optional proxy password. Never echoed in logs.</summary>
    public string? ProxyPassword
    {
        get => GetString(ProxyPasswordKey);
        set => this[ProxyPasswordKey] = value!;
    }

    /// <summary>
    /// When <see langword="true"/>, the server certificate chain is validated but
    /// hostname verification is skipped. Weakens transport security; development only.
    /// </summary>
    public bool AllowSelfSignedCerts
    {
        get => GetBoolean(AllowSelfSignedKey, false);
        set => this[AllowSelfSignedKey] = value;
    }

    /// <summary>
    /// Overrides the <c>User-Agent</c> header sent on every HTTP request.
    /// Leave empty to let the library choose automatically based on the transport:
    /// <see cref="BricksNetDefaults.ThriftUserAgent"/> for Thrift, or
    /// <c>BricksNet.dotnet/&lt;version&gt;</c> for REST.
    /// </summary>
    /// <remarks>
    /// When set, use the RFC 7231 <c>Product/Version</c> format (slash separator,
    /// e.g. <c>SimbaJDBCDriver/2.6.38</c>). A space-separated value such as
    /// <c>SimbaJDBCDriver 2.6.38</c> will be silently normalized and a debug-level
    /// warning will be logged.
    /// </remarks>
    public string? UserAgent
    {
        get => GetString(UserAgentKey);
        set => this[UserAgentKey] = value!;
    }

    /// <inheritdoc />
    [AllowNull]
    public override object this[string keyword]
    {
        get => base[Canonicalize(keyword)];
        set => base[Canonicalize(keyword)] = value!;
    }

    /// <inheritdoc />
    public override bool ContainsKey(string keyword) => base.ContainsKey(Canonicalize(keyword));

    /// <inheritdoc />
    public override bool Remove(string keyword) => base.Remove(Canonicalize(keyword));

    /// <inheritdoc />
    public override bool TryGetValue(string keyword, out object value) =>
        base.TryGetValue(Canonicalize(keyword), out value!);

    /// <summary>
    /// Projects the current keyword values onto a <see cref="BricksNetClientOptions"/> record.
    /// The access token (carried separately so it is not retained on the long-lived options
    /// record) is returned via <paramref name="accessToken"/>.
    /// </summary>
    /// <param name="accessToken">Receives the parsed access token, or <see langword="null"/>.</param>
    /// <returns>The populated client options.</returns>
    /// <exception cref="BricksNetException">A required keyword is missing.</exception>
    public BricksNetClientOptions ToClientOptions(out string? accessToken)
    {
        var host = Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new BricksNetException("The connection string is missing the required 'Host' keyword.");
        }

        var httpPath = HttpPath;
        if (string.IsNullOrWhiteSpace(httpPath))
        {
            throw new BricksNetException("The connection string is missing the required 'HttpPath' keyword.");
        }

        accessToken = Token;

        return new BricksNetClientOptions
        {
            Host = host,
            Port = Port,
            HttpPath = httpPath,
            AuthType = AuthType,
            UseThrift = UseThrift,
            ProxyOptions = BuildProxyOptions(),
            SslOptions = new SslOptions { AllowSelfSignedCerts = AllowSelfSignedCerts },
            UserAgent = string.IsNullOrEmpty(UserAgent) ? null : UserAgent,
        };
    }

    /// <summary>Builds the per-session defaults expressed by this connection string.</summary>
    /// <returns>The populated session options.</returns>
    public SessionOptions ToSessionOptions() => new()
    {
        Catalog = Catalog,
        Schema = Schema,
        QueryTimeoutSeconds = QueryTimeoutSeconds,
        MaxRows = MaxRows,
    };

    private ProxyOptions? BuildProxyOptions()
    {
        var proxyUri = ProxyUri;
        if (string.IsNullOrWhiteSpace(proxyUri))
        {
            return null;
        }

        var user = ProxyUser;
        NetworkCredential? credentials = string.IsNullOrEmpty(user)
            ? null
            : new NetworkCredential(user, ProxyPassword ?? string.Empty);

        return new ProxyOptions
        {
            Uri = new Uri(proxyUri, UriKind.Absolute),
            Type = ProxyType,
            Credentials = credentials,
        };
    }

    private static string Canonicalize(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        return KeywordAliases.TryGetValue(keyword, out var canonical) ? canonical : keyword;
    }

    private string? GetString(string keyword) =>
        base.TryGetValue(keyword, out var value) ? value as string : null;

    private int GetInt32(string keyword, int defaultValue) =>
        base.TryGetValue(keyword, out var value)
        && int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    private bool GetBoolean(string keyword, bool defaultValue) =>
        base.TryGetValue(keyword, out var value) && bool.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : defaultValue;

    private TEnum GetEnum<TEnum>(string keyword, TEnum defaultValue)
        where TEnum : struct, Enum =>
        base.TryGetValue(keyword, out var value) && Enum.TryParse<TEnum>(value?.ToString(), ignoreCase: true, out var parsed)
            ? parsed
            : defaultValue;
}
