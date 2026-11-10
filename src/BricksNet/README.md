# BricksNet

A Databricks SQL client for .NET 10, with two ways to use it:

- **Fluent async API**: `BricksNetClient` → `BricksNetSession` → `BricksNetOperation`
- **ADO.NET provider**: `BricksNetConnection`, `BricksNetCommand`, `BricksNetDataReader`. Works with Dapper.

Both work against **SQL Warehouses** (REST backend) and **all-purpose clusters** (Thrift backend).
Results arrive as Arrow, inline JSON or CloudFetch (for results over 25 MiB). Authentication supports
personal access tokens, OAuth client credentials (M2M) and OAuth authorization code with PKCE.

For an Entity Framework Core provider, see
[BricksNet.EntityFrameworkCore](https://www.nuget.org/packages/BricksNet.EntityFrameworkCore).

## Install

```shell
dotnet add package BricksNet --prerelease
```

## Fluent API

```csharp
using BricksNet;

var options = new BricksNetClientOptions
{
    Host     = "adb-<workspace-id>.azuredatabricks.net",
    HttpPath = "/sql/1.0/warehouses/<warehouse-id>",
};

await using var client = new BricksNetClient(options, accessToken: "<pat>");
await using var session = await client.OpenSessionAsync();
await using var operation = await session.ExecuteStatementAsync("SELECT 1 AS n");

var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();
```

## ADO.NET with Dapper

```csharp
using System.Data;
using BricksNet.AdoNet;
using Dapper;

await using IDbConnection conn = new BricksNetConnection(
    "Host=adb-<workspace-id>.azuredatabricks.net;HttpPath=/sql/1.0/warehouses/<id>;Token=<pat>");
await conn.OpenAsync();

var rows = await conn.QueryAsync("SELECT id, name FROM main.sales.orders LIMIT 100");
```

## Dependency injection

```csharp
builder.Services.AddBricksNet(
    new BricksNetClientOptions { Host = "...", HttpPath = "..." },
    accessToken: builder.Configuration["Databricks:Token"]!);
```

Then inject `IBricksNetClient`. To use the Thrift backend, set `UseThrift = true` and use a cluster
HTTP path such as `/sql/protocolv1/o/<org-id>/<cluster-id>`.

Logging goes through `Microsoft.Extensions.Logging`. OpenTelemetry spans are emitted under the
`BricksNet` activity source.

## Documentation

- [Full documentation and examples](https://github.com/jogyamfi/BricksNet#readme)
- [Changelog](https://github.com/jogyamfi/BricksNet/blob/main/CHANGELOG.md)
- [Report an issue](https://github.com/jogyamfi/BricksNet/issues)

Licensed under [Apache 2.0](https://github.com/jogyamfi/BricksNet/blob/main/LICENSE).
