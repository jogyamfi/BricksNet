# BricksNet.EntityFrameworkCore — Design and Status

> User guide: [`docs/EFCORE.md`](EFCORE.md). Core library plan: [`PLAN.md`](../PLAN.md).

`BricksNet.EntityFrameworkCore` is an Entity Framework Core 10 relational provider for Databricks SQL.
It is a separate project and NuGet package on top of the BricksNet ADO.NET provider
(`BricksNetConnection`, `BricksNetCommand`, `BricksNetDataReader`).

**Status:** all nine planned phases are implemented. The live functional suite
(`tests/BricksNet.EntityFrameworkCore.FunctionalTests`) passes against a Databricks workspace
(2026-10-03). CI runs the unit and SQL-baseline tests; the live suite runs locally.

## Scope

| Supported | Not supported |
|---|---|
| LINQ query translation, raw SQL (`FromSql`, `SqlQuery`, `ExecuteSql`) | Migrations (`Migrate`, `dotnet ef migrations`) |
| `SaveChanges` (INSERT/UPDATE/DELETE on Delta tables), optimistic concurrency | Reverse engineering (`dotnet ef dbcontext scaffold`) |
| `ExecuteUpdate` / `ExecuteDelete` | Multi-statement transactions (Databricks has none) |
| Unity Catalog three-part names | `ARRAY`/`MAP`/`STRUCT`/`VARIANT`/`INTERVAL` mappings |
| `EnsureCreated` / `EnsureDeleted` / `GenerateCreateScript` for development and tests | Spatial types, full-text search |

## Key decisions

1. **No transactions.** `BeginTransaction`, `UseTransaction` and `TransactionScope` throw.
   `SaveChanges` runs its statements without a transaction (`BricksNetBatchExecutor`) and is not
   atomic across statements. The provider logs `BricksNetEventId.NonAtomicSaveChanges` and marks the
   entities of already-committed statements as saved when a later one fails.
2. **No database-generated values.** Databricks has no `RETURNING`/`OUTPUT`, so keys and defaults
   are set in .NET. Integer keys are not treated as generated. `Guid` keys get UUIDv7 values. Model
   validation rejects store-generated configuration with guidance.
3. **Rows affected from the DML summary.** Databricks answers DML with a `num_affected_rows` result
   set. Every command is mapped as a rows-affected-only result set, which gives EF its concurrency
   checks.
4. **Core fixes in BricksNet where EF needed them** (Phase 0). These also benefit Dapper and other
   ADO.NET users.
5. **Testing.** CI runs unit and SQL-baseline tests against an in-memory fake service. A purpose-built
   live suite (`Category=E2E`) runs locally against a real workspace.
6. **EF Core version range `[10.0.12, 11.0.0)`.** The provider uses EF "internal" APIs, as all
   providers do, which can change in a major release.

## Databricks dialect facts behind the design

| Fact | How the provider handles it |
|---|---|
| No multi-statement transactions; each statement commits on its own | Decision 1 |
| DML returns a summary result set (`num_affected_rows`, …) | `ExecuteNonQuery`/`RecordsAffected` read it (Phase 0); EF consumes it as rows affected (Phase 6) |
| No `RETURNING`/`OUTPUT` | Decision 2 |
| Double quotes delimit strings; identifiers use backticks | `BricksNetSqlGenerationHelper` |
| String literals use backslash escapes; `'a''b'` is two literals | One escaping helper, covered by fuzz tests |
| Named parameter markers `:name` | Parameter placeholders `:p0` |
| `/` on integers returns a fraction | Integer division as `CAST(a DIV b AS INT)` |
| `+` does not concatenate strings | `\|\|` |
| `LIMIT`/`OFFSET` must be constant | `Take`/`Skip` values inlined as literals |
| No `APPLY`; `LATERAL` joins exist | `CROSS JOIN LATERAL` / `LEFT JOIN LATERAL … ON TRUE` |
| No `UPDATE … FROM` | `UPDATE … WHERE EXISTS (…)` |
| Constraints are informational and Unity Catalog-only; no indexes | DDL omits constraints and indexes; unique indexes log a warning |
| Concurrent Delta writers can conflict (`DELTA_CONCURRENT_*`) | `EnableRetryOnFailure`, retrying each failed statement on its own |
| Each statement is a network round trip | Multi-row inserts, kept sessions, bulk operations |
| Three-part names `catalog.schema.table` | `HasDefaultCatalog` / `HasCatalog` |
| No unsigned integers; `TINYINT` is signed | Unsigned types widen (`byte` → `SMALLINT`, `ulong` → `DECIMAL(20,0)`) |
| `round` rounds half up; .NET rounds half to even | `Math.Round` → `bround` |

## What each phase delivered

| Phase | Delivered | Changed from the original plan |
|---|---|---|
| 0 — Core prerequisites | Rows affected for DML; typed `GetFieldValue<T>`; `TIMESTAMP_NTZ` → `DateTime`; `DbType` → SQL type hints; `ServerVersion`/`SparkVersion`; `BricksNetException.ErrorClass`/`IsTransient`; instance client factories; `KeepSessionAlive` | `SparkVersion` (from `SELECT version()`) instead of a DBR version, which SQL warehouses do not report |
| 1 — Scaffolding | Project and package, `UseBricksNet` overloads, service registration, transaction policy, SQL-baseline test harness | Repeated `UseBricksNet` calls replace earlier connection settings rather than being rejected; a throwing migrator gives a clear "migrations not supported" error |
| 2 — Type mapping | Every scalar CLR type, Databricks typed literals, facets and store-type aliases | Decimal parameters keep their own precision instead of the column's |
| 3 — Query pipeline | `LIMIT`/`OFFSET`, `DIV`, `\|\|`, `LATERAL`; baselines for all core operators | `Take`/`Skip` inlined as literals; no unsupported-shape detector, because the live run showed which shapes work |
| 4 — Translators | String, math, date, conversion, `Guid`, `Regex` translations; `EF.Functions.ILike`/`DateDiff*`; `string.Join` over groups | `Math.Max`/`Min` and `byte[].Length` live in the SQL-translating visitor, where EF 10 routes them |
| 5 — Model | Value-generation convention, UUIDv7 keys, model validation, Unity Catalog names | `HasDefaultValue` and hidden `OwnsMany` keys are also rejected |
| 6 — SaveChanges | DML generation, concurrency checks, non-atomic handling, retries, diagnostic events | Retries are tagged per statement, because EF's strategy unwraps `DbUpdateException`; `ExecuteSql` must be wrapped to be retried |
| 7 — Bulk and performance | `ExecuteUpdate`/`ExecuteDelete`, multi-row inserts, idle-session replacement | `WHERE EXISTS` instead of generated `MERGE`; an idle timeout instead of a session pool |
| 8 — Live testing | `EnsureCreated`/`EnsureDeleted`/`GenerateCreateScript`; live functional suite with benchmarks | A purpose-built suite instead of EF's specification tests; `EnsureDeleted` drops tables, not schemas |
| 9 — Release | User guide, sample, packaging, CI pack step | — |

## Architecture

```
src/BricksNet.EntityFrameworkCore/
  Extensions/      UseBricksNet, AddEntityFrameworkBricksNet, HasDefaultCatalog/HasCatalog, EF.Functions
  Infrastructure/  options builder and extension, model validator
  Diagnostics/     BricksNetEventId, logging definitions
  Metadata/        value-generation convention, catalog annotations
  Storage/         connection, SQL generation helper, type mappings, database creator, transient-error detection
  Query/           SQL generator, parameter processor, SQL-translating visitor, method/member/aggregate translators
  Update/          update SQL generator, multi-row insert batch, non-transactional batch executor
  Migrations/      DDL generator (tables only), throwing migrator and history repository
  ValueGeneration/ UUIDv7 key generator
tests/BricksNet.EntityFrameworkCore.Tests/             unit and SQL-baseline tests (CI)
tests/BricksNet.EntityFrameworkCore.FunctionalTests/   live suite (local)
```

## Known limitations

- `SaveChanges` with several statements is not atomic. Inserts into a single table are, because they
  become one statement.
- `ExecuteUpdate` cannot set a column from another table's values; use `MERGE INTO` via `ExecuteSql`.
- `ExecuteSql` is not retried unless it is run through `CreateExecutionStrategy()`.
- `TimeSpan` is stored as text, so ordering and comparisons can be wrong for values with days or
  negative values.
- `DateTimeOffset` date parts follow the session time zone (UTC by default).
- Not translated: `TrimStart(char)`/`TrimEnd(char)`, `Millisecond`, `DateTime.ToString()`, and the less
  common `MidpointRounding` modes. `string.Join` over an ordered group is joined on the client.
- The 256-parameter cap per multi-row insert is conservative.

See [`docs/EFCORE.md`](EFCORE.md) for the full user-facing list.

## Future work

- Run EF Core's relational specification tests (`Microsoft.EntityFrameworkCore.Relational.Specification.Tests`)
  against a workspace, for broader coverage than the purpose-built suite.
- Migrations and reverse engineering (Unity Catalog `information_schema`).
- Mappings for `ARRAY`, `MAP`, `STRUCT` and primitive collections.
- Generate `MERGE INTO` for `ExecuteUpdate` with values from other tables.
- Raise the multi-row insert parameter cap once the service limits are measured.
- Add the live suite to a scheduled CI job if a workspace becomes available to CI.

## Running the live suite

```powershell
$env:BRICKSNET_EF_HOST     = "adb-<workspace-id>.azuredatabricks.net"
$env:BRICKSNET_EF_HTTPPATH = "/sql/1.0/warehouses/<warehouse-id>"
$env:BRICKSNET_EF_TOKEN    = "<personal access token>"
$env:BRICKSNET_EF_CATALOG  = "<catalog where a temporary schema may be created>"
# Optional, for an all-purpose cluster: $env:BRICKSNET_EF_USETHRIFT = "true"
dotnet test tests/BricksNet.EntityFrameworkCore.FunctionalTests --logger "console;verbosity=detailed"
```

The token needs `USE CATALOG` and `CREATE SCHEMA` on the catalog. Each run creates and drops a
temporary `efct_<timestamp>_<id>` schema. `--filter Category=Benchmark` runs only the timing tests.
Run the suite before every release.
