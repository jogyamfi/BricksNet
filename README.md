# BricksNet
<!---
[![NuGet](https://img.shields.io/nuget/v/BricksNet.svg)](https://www.nuget.org/packages/BricksNet)
[![Build](https://github.com/your-org/bricksnet/actions/workflows/build.yml/badge.svg)](https://github.com/your-org/bricksnet/actions/workflows/build.yml)
-->
**BricksNet** is a .NET 10 Databricks SQL client library providing two complementary surfaces:

- **Fluent async API** — `BricksNetClient → BricksNetSession → BricksNetOperation`
- **ADO.NET provider** — `BricksNetConnection`, `BricksNetCommand`, `BricksNetDataReader`; compatible with Dapper and EF Core (read)

Both surfaces target SQL Warehouses (REST backend) and All-purpose clusters (Thrift backend).

---

## Installation
This is not published yet and so not yet on Nuget
```shell
dotnet add package BricksNet
```

---

## Quick Start — Fluent API

### Personal Access Token (PAT)

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
foreach (var row in rows)
    Console.WriteLine(row["n"]);
```

### OAuth 2.0 PKCE (browser redirect)

```csharp
using BricksNet;
using BricksNet.Auth;
using BricksNet.Contracts.Options;

var options = new BricksNetClientOptions
{
    Host      = "adb-<workspace-id>.azuredatabricks.net",
    HttpPath  = "/sql/1.0/warehouses/<warehouse-id>",
    AuthType  = AuthType.OAuthPkce,
    UseThrift = false,
};

// A browser window opens for the user to authorise the application.
var oauthOptions = new OAuthOptions
{
    ClientId              = "<service-principal-client-id>",
    AuthorizationEndpoint = new Uri("https://adb-<workspace-id>.azuredatabricks.net/oidc/v1/authorize"),
    TokenEndpoint         = new Uri("https://adb-<workspace-id>.azuredatabricks.net/oidc/v1/token"),
    Scopes                = ["sql"],
};

await using var client = new BricksNetClient(options, oauthOptions);
await using var session = await client.OpenSessionAsync();
```

---

## Quick Start — ADO.NET

### Connection string format

```
Host=adb-<workspace-id>.azuredatabricks.net;HttpPath=/sql/1.0/warehouses/<id>;Token=<pat>
```

For OAuth:
```
Host=...;HttpPath=...;ClientId=<id>;ClientSecret=<secret>
```

Add `KeepSessionAlive=true` to keep the Databricks session open across `Close()`/`Open()` cycles
(it is released on `Dispose()`), which avoids starting a new session each time the connection opens.

`ExecuteNonQuery` returns the rows affected by `INSERT`/`UPDATE`/`DELETE`/`MERGE`. A failed statement
throws `BricksNetException`. Its `ErrorClass` holds the Databricks error class, and `IsTransient`
flags errors that are safe to retry, such as Delta concurrent-write conflicts.

### Example with Dapper

```csharp
using System.Data;
using BricksNet.AdoNet;
using Dapper;

await using IDbConnection conn = new BricksNetConnection(
    "Host=adb-xxx.azuredatabricks.net;HttpPath=/sql/1.0/warehouses/yyy;Token=dapi...");
await conn.OpenAsync();

var rows = await conn.QueryAsync("SELECT id, name FROM my_catalog.my_schema.my_table LIMIT 100");
foreach (var row in rows)
    Console.WriteLine($"{row.id}: {row.name}");
```

---

## Entity Framework Core (preview)

`BricksNet.EntityFrameworkCore` is an EF Core 10 provider built on the ADO.NET layer. It is under
active development (see [docs/EFCORE_PLAN.md](docs/EFCORE_PLAN.md)). Today it supports raw SQL and
basic LINQ queries. `SaveChanges`, migrations and `EnsureCreated` are not supported yet.

```csharp
services.AddDbContext<SalesContext>(options => options.UseBricksNet(
    "Host=adb-<workspace-id>.azuredatabricks.net;HttpPath=/sql/1.0/warehouses/<id>;Token=<pat>;Catalog=main;Schema=sales"));

var recent = await db.Orders.Where(o => o.Region == region).ToListAsync();
var updated = await db.Database.ExecuteSqlAsync($"UPDATE orders SET status = {status} WHERE id = {id}");
```

Databricks has no multi-statement transactions, so `BeginTransaction` and `TransactionScope` throw
instead of silently doing nothing.

---

## Dependency Injection

```csharp
using BricksNet;

builder.Services.AddBricksNet(
    new BricksNetClientOptions
    {
        Host     = builder.Configuration["Databricks:Host"]!,
        HttpPath = builder.Configuration["Databricks:HttpPath"]!,
    },
    accessToken: builder.Configuration["Databricks:Token"]!);
```

Inject `IBricksNetClient` into your services.

---

## Thrift Backend (All-purpose clusters)

Set `UseThrift = true` on `BricksNetClientOptions`:

```csharp
var options = new BricksNetClientOptions
{
    Host      = "...",
    HttpPath  = "/sql/protocolv1/o/<org-id>/<cluster-id>",
    UseThrift = true,
};
```

### Metadata differences between backends

`GetCatalogsAsync`, `GetSchemasAsync`, `GetTablesAsync` and `GetColumnsAsync` return JDBC-style
columns (`TABLE_CAT`, `TABLE_SCHEM`, `TABLE_NAME`, `COLUMN_NAME`, …) on Thrift. The REST API has no
metadata calls, so the REST backend runs `SHOW CATALOGS` / `SHOW SCHEMAS` / `SHOW TABLES` /
`SHOW COLUMNS` and returns their Databricks SQL columns instead. On REST, `GetColumnsAsync` requires a
table name and ignores the column pattern, and `%` in patterns is translated to the `*` wildcard.

---

## Logging & Telemetry

BricksNet writes structured logs via `Microsoft.Extensions.Logging` and emits OpenTelemetry-compatible `Activity` spans under the `BricksNet` source name. Register your preferred sink in the normal way.

---

## License

[Apache 2.0](LICENSE)
