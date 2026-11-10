# BricksNet.EntityFrameworkCore

An Entity Framework Core 10 provider for Databricks SQL, built on
[BricksNet](https://www.nuget.org/packages/BricksNet). **Preview.**

It supports LINQ queries, raw SQL (`FromSql`, `SqlQuery`, `ExecuteSql`), `SaveChanges`,
`ExecuteUpdate`/`ExecuteDelete`, and `EnsureCreated`/`EnsureDeleted` for development, all over Delta tables.

## Install

```shell
dotnet add package BricksNet.EntityFrameworkCore --prerelease
```

## Configure

```csharp
services.AddDbContext<SalesContext>(options => options.UseBricksNet(
    "Host=adb-<workspace-id>.azuredatabricks.net;HttpPath=/sql/1.0/warehouses/<id>;Token=<pat>;Catalog=main;Schema=sales",
    o => o.EnableRetryOnFailure()));
```

`UseBricksNet` also accepts `BricksNetClientOptions` (for OAuth, Thrift or proxies) or an existing
`BricksNetConnection`.

```csharp
var recent = await db.Orders.Where(o => o.Region == region).ToListAsync();
db.Orders.Add(new Order { Id = Guid.CreateVersion7(), Region = region });
await db.SaveChangesAsync();
```

## Differences from SQL Server and other providers

- **No transactions or migrations.** `BeginTransaction`, `TransactionScope` and migrations throw
  `NotSupportedException`. A `SaveChanges` that runs several statements is not atomic: if one fails,
  the earlier ones stay committed.
- **No database-generated values.** Set keys and defaults in .NET. Integer keys are not
  auto-generated, and `Guid` keys get version-7 values.
- **Optimistic concurrency** works with properties you maintain yourself (`IsConcurrencyToken`).

## Documentation

- [EF Core provider guide](https://github.com/jogyamfi/BricksNet/blob/main/docs/EFCORE.md):
  configuration, type mapping, LINQ translations and limitations
- [Changelog](https://github.com/jogyamfi/BricksNet/blob/main/CHANGELOG.md)
- [Report an issue](https://github.com/jogyamfi/BricksNet/issues)

Licensed under [Apache 2.0](https://github.com/jogyamfi/BricksNet/blob/main/LICENSE).
