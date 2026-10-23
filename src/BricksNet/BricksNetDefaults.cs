namespace BricksNet;

/// <summary>
/// Well-known constant values used by the BricksNet library.
/// </summary>
public static class BricksNetDefaults
{
    /// <summary>
    /// The leading <c>User-Agent</c> product token used when connecting via the Thrift (HiveServer2)
    /// transport. BricksNet's own identifier (<c>BricksNet.dotnet/&lt;version&gt;</c>) is appended
    /// after it.
    /// </summary>
    /// <remarks>
    /// The SQL warehouse proxy accepts Thrift requests only when the first User-Agent token names a
    /// known Databricks driver (for example <c>DatabricksJDBCDriverOSS</c>,
    /// <c>ADBCDatabricksDriver</c> or <c>SimbaSparkJDBCDriver</c>); anything else is rejected with
    /// <c>MALFORMED_REQUEST: Client ... is not supported for SQL warehouses</c>. Despite that message,
    /// <c>SimbaJDBCDriver/&lt;any version&gt;</c> is rejected too. Tokens after the first are ignored.
    /// Override this behaviour by setting
    /// <see cref="BricksNet.Contracts.Options.BricksNetClientOptions.UserAgent"/> explicitly.
    /// The REST backend (SQL Statement Execution API) does not enforce this constraint.
    /// </remarks>
    public const string ThriftUserAgent = "DatabricksJDBCDriverOSS/3.0.0";
}
