namespace BricksNet.Contracts.Options;

/// <summary>Top-level connection and behaviour options for a <c>BricksNetClient</c>.</summary>
public sealed record BricksNetClientOptions
{
    /// <summary>Databricks workspace hostname (e.g. <c>my-workspace.azuredatabricks.net</c>).</summary>
    public required string Host { get; init; }

    /// <summary>HTTPS port. Defaults to <c>443</c>.</summary>
    public int Port { get; init; } = 443;

    /// <summary>HTTP path to the SQL warehouse or cluster (e.g. <c>/sql/1.0/warehouses/&lt;id&gt;</c>).</summary>
    public required string HttpPath { get; init; }

    /// <summary>Authentication mechanism. Defaults to <see cref="AuthType.Pat"/>.</summary>
    public AuthType AuthType { get; init; } = AuthType.Pat;

    /// <summary>When <see langword="true"/>, the Thrift backend is used; otherwise the REST backend.</summary>
    public bool UseThrift { get; init; } = true;

    /// <summary>HTTP retry policy.</summary>
    public RetryOptions RetryOptions { get; init; } = new();

    /// <summary>Optional proxy configuration. <see langword="null"/> means no proxy.</summary>
    public ProxyOptions? ProxyOptions { get; init; }

    /// <summary>TLS configuration.</summary>
    public SslOptions SslOptions { get; init; } = new();

    /// <summary>Telemetry and diagnostics configuration.</summary>
    public TelemetryOptions TelemetryOptions { get; init; } = new();

    /// <summary>
    /// Overrides the <c>User-Agent</c> header sent on every HTTP request.
    /// Leave <see langword="null"/> to use the library default (<c>BricksNet.dotnet/&lt;version&gt;</c>).
    /// Set to a recognised driver string (e.g. <c>SimbaJDBCDriver/2.6.38</c>) when the target
    /// SQL warehouse rejects the default client identifier.
    /// </summary>
    public string? UserAgent { get; init; }
}
