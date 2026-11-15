# BricksNet

[![Build](https://github.com/jogyamfi/BricksNet/actions/workflows/build.yml/badge.svg)](https://github.com/jogyamfi/BricksNet/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
<!-- Enable after the first release to nuget.org:
[![NuGet](https://img.shields.io/nuget/v/BricksNet.svg)](https://www.nuget.org/packages/BricksNet)
-->

> **Status: preview.** The API can still change before 1.0. BricksNet is an independent,
> community-maintained project. It is not affiliated with, endorsed by or supported by Databricks, Inc.

**BricksNet** is a .NET 10 Databricks SQL client library providing three complementary surfaces:

- **Fluent async API** — `BricksNetClient → BricksNetSession → BricksNetOperation`
- **ADO.NET provider** — `BricksNetConnection`, `BricksNetCommand`, `BricksNetDataReader`; compatible with Dapper
- **EF Core provider** (preview) — `BricksNet.EntityFrameworkCore`: LINQ, `SaveChanges` and bulk operations over Delta tables ([guide](docs/EFCORE.md))

All surfaces target SQL Warehouses (REST backend) and All-purpose clusters (Thrift backend).

---

## Installation

The packages are not on nuget.org yet. Until the first preview release, build from source
(`dotnet build BricksNet.sln`) and reference the projects under `src/`. After the release:

```shell
dotnet add package BricksNet --prerelease
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

Connection strings authenticate with a personal access token. OAuth client credentials
(machine-to-machine) are not supported. To use OAuth PKCE with ADO.NET, build the client with
`OAuthOptions` (see above) and pass it to the `BricksNetConnection` constructor that takes a client
factory.

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

`BricksNet.EntityFrameworkCore` is an EF Core 10 provider built on the ADO.NET layer. It supports LINQ
queries, raw SQL, `SaveChanges`, `ExecuteUpdate`/`ExecuteDelete`, and `EnsureCreated`/`EnsureDeleted`
for development. Migrations are not supported. See the **[EF Core provider guide](docs/EFCORE.md)**
for configuration, type mapping, translations and limitations. The design and roadmap are in
[docs/EFCORE_PLAN.md](docs/EFCORE_PLAN.md).

```shell
dotnet add package BricksNet.EntityFrameworkCore --prerelease
```

```csharp
services.AddDbContext<SalesContext>(options => options.UseBricksNet(
    "Host=adb-<workspace-id>.azuredatabricks.net;HttpPath=/sql/1.0/warehouses/<id>;Token=<pat>;Catalog=main;Schema=sales",
    o => o.EnableRetryOnFailure()));

var recent = await db.Orders.Where(o => o.Region == region).ToListAsync();
db.Orders.Add(new Order { Id = Guid.CreateVersion7(), Region = region });
await db.SaveChangesAsync();
```

Things that differ from SQL Server:

- **No multi-statement transactions.** `BeginTransaction` and `TransactionScope` throw. A
  `SaveChanges` that runs several statements is not atomic: if one fails, earlier ones stay committed
  (and are marked as saved).
- **No database-generated values.** Keys and defaults must be set in .NET. Integer keys are not
  auto-generated, and `Guid` keys get version-7 values.
- **Optimistic concurrency** works with properties you maintain yourself (`IsConcurrencyToken`), using
  the affected-row count Databricks reports.

Try it with the sample app: `dotnet run --project samples/BricksNet.Samples -- EfCore`.

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

### Timestamp precision on REST

Databricks stores timestamps to the microsecond. The REST API only allows inline results as
`JSON_ARRAY`, which renders `TIMESTAMP` and `TIMESTAMP_NTZ` values to the **millisecond**, so REST
without CloudFetch returns millisecond precision. Thrift, and REST with `UseCloudFetch = true`, return
Arrow results with full microsecond precision.

---

## Logging & Telemetry

BricksNet writes structured logs via `Microsoft.Extensions.Logging` and emits OpenTelemetry-compatible `Activity` spans under the `BricksNet` source name. Register your preferred sink in the normal way.

---

## Live testing

The default `dotnet test` run is offline. To run the live suites against a real (including free)
Databricks workspace, see **[docs/LIVE_TESTING.md](docs/LIVE_TESTING.md)**.

---

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test and send changes. To report a
vulnerability, follow [SECURITY.md](SECURITY.md) and do not open a public issue.

---

## License

[Apache 2.0](LICENSE). Third-party attributions are in [NOTICE](NOTICE).

Databricks is a trademark of Databricks, Inc. This project is not affiliated with Databricks, Inc.
