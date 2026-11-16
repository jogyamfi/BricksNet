# User-Agent header

BricksNet sets the HTTP `User-Agent` header on every request. Databricks shows it in **Query
History**, so it tells you which client ran a statement.

## Defaults

| Backend | Header sent |
|---|---|
| REST (SQL Statement Execution API) | `BricksNet.dotnet/<version>` |
| Thrift | `DatabricksJDBCDriverOSS/3.0.0 BricksNet.dotnet/<version>` |

`<version>` is the package version without build metadata, for example `0.1.0-preview.1`.

### Why the Thrift header leads with another driver's name

The SQL warehouse endpoint accepts Thrift requests only when the first `User-Agent` token names a
Databricks driver it recognises. Any other client is rejected with
`MALFORMED_REQUEST: Client ... is not supported for SQL warehouses`. BricksNet therefore leads with
the token in `BricksNetDefaults.ThriftUserAgent` and puts its own identifier second, so it is still
visible in the header. The REST API has no such check, so REST requests identify only as BricksNet.
BricksNet sends the same Thrift header to SQL warehouses and all-purpose clusters.

## Setting your own value

Set `BricksNetClientOptions.UserAgent`, or the `UserAgent` connection-string keyword:

```
Host=...;HttpPath=...;Token=...;UserAgent=MyApp/2.1.0
```

An explicit value replaces the default completely, on both backends. On Thrift against a SQL
warehouse, the first token must still be one the warehouse accepts, or requests fail as described
above.

Use the `Product/Version` form. A space-separated value such as `MyApp 2.1.0` is rewritten to
`MyApp/2.1.0`, and a debug-level log message records the change.
