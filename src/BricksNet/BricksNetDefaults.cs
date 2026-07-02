namespace BricksNet;

/// <summary>
/// Well-known constant values used by the BricksNet library.
/// </summary>
public static class BricksNetDefaults
{
    /// <summary>
    /// The <c>User-Agent</c> value automatically used when connecting via the Thrift (HiveServer2)
    /// transport. Databricks SQL Warehouses require a Simba JDBC driver version ≥ 2.6.14 or ODBC
    /// ≥ 2.6.15 when connecting over this protocol.
    /// </summary>
    /// <remarks>
    /// Override this behaviour by setting
    /// <see cref="BricksNet.Contracts.Options.BricksNetClientOptions.UserAgent"/> explicitly.
    /// The REST backend (SQL Statement Execution API) does not enforce this constraint; BricksNet's
    /// own default identifier (<c>BricksNet.dotnet/&lt;version&gt;</c>) is used there.
    /// </remarks>
    public const string ThriftUserAgent = "SimbaJDBCDriver/2.6.38";
}
