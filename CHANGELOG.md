# Changelog

All notable changes to **BricksNet** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Fluent async API: `BricksNetClient`, `BricksNetSession`, `BricksNetOperation`.
- ADO.NET provider: `BricksNetConnection`, `BricksNetCommand`, `BricksNetDataReader`, `BricksNetParameter`, `BricksNetTransaction`.
- REST backend (Databricks SQL Exec API) with Arrow IPC, inline-JSON, and CloudFetch result modes.
- Thrift backend (TCLIService binary protocol) for All-purpose clusters.
- LZ4 decompression for compressed result payloads.
- Authentication: Personal Access Token (PAT), OAuth Client Credentials (M2M), OAuth Authorization Code + PKCE.
- `IHttpClientFactory`-based HTTP transport with retry (HTTP 429/503) and configurable timeout.
- `ILogger<T>` structured logging and OpenTelemetry `ActivitySource` spans.
- `AddBricksNet` extension for `IServiceCollection` DI registration.
- Integration test harness: in-process Kestrel fake REST service (`FakeService`) + `FixtureLibrary`.
- Thrift offline unit tests via `ThriftBinaryEncoder` and `FakeThriftTransport`.
- NuGet packaging with Source Link (`.snupkg` symbols) and Codecov coverage reporting.
- GitHub Actions CI: build, test (non-E2E), coverage upload, and Release pack.
- GitHub Actions security workflow: vulnerable-package scan + OWASP Dependency-Check.
- `IBricksNetOperation.WaitForCompletionAsync` — waits for a terminal state without fetching rows.
- `FetchResult.HasMoreRows` — backends signal further result pages.
- `BricksNetClientOptions.UseCloudFetch` / `UseCloudFetch` connection-string keyword — REST results via
  `EXTERNAL_LINKS` + `ARROW_STREAM`, required for results over 25 MiB.
- `IBricksNetOperation.GetSchemaAsync` — server-reported column metadata.
- OAuth: refresh-token grant renews expired access tokens without reopening the browser.
- `OAuthOptions.RedirectHost` (default `localhost`).
- `ExecuteNonQuery` returns the number of rows changed by `INSERT`/`UPDATE`/`DELETE`/`MERGE` (from
  Databricks' `num_affected_rows` summary), and `BricksNetDataReader.RecordsAffected` reports it for
  DML results. Other statements still return `-1`.
- `BricksNetException.ErrorClass` (e.g. `DELTA_CONCURRENT_APPEND`), parsed from the server message, and
  `IsTransient` (HTTP 429/503 and Delta concurrent-transaction conflicts) for retry policies.
- `BricksNetConnection.GetServerVersionAsync`, `ServerVersion` and `SparkVersion`: the server's Apache
  Spark version, queried once (`SELECT version()`) and cached.
- `KeepSessionAlive` connection-string keyword / `BricksNetConnection.KeepSessionAlive`: `Close()` keeps
  the session for the next `Open()` to reuse; `Dispose()` releases it.
- `BricksNetConnection` constructors that take a client factory, as a parallel-safe alternative to the
  static `ClientFactory`.
- `BricksNet.EntityFrameworkCore` (preview): an EF Core 10 provider. `UseBricksNet(...)` accepts a
  connection string, an existing `BricksNetConnection` or `BricksNetClientOptions`. Raw SQL
  (`ExecuteSql`, `SqlQuery`, `FromSql`) and basic LINQ queries work, using backtick identifiers, `:name`
  parameters and Databricks string-literal escaping. Sessions are kept alive across EF's connection
  open/close by default. Transactions and migrations throw
  `NotSupportedException`.
  - Type mapping covers every scalar CLR type. It uses Databricks typed literals (`5Y`, `5S`, `5L`,
    `1.5F`, `1.5D`, `1.5BD`, `DATE'…'`, `TIMESTAMP_NTZ'…'`, `TIMESTAMP'…'`, `X'…'`). Unsigned types
    widen to the next signed type (`ulong` → `DECIMAL(20,0)`). `Guid`, `char`, `TimeOnly` and
    `TimeSpan` are stored as `STRING`. `DateTime` maps to `TIMESTAMP_NTZ` and `DateTimeOffset` to
    `TIMESTAMP` (UTC). `decimal` defaults to `DECIMAL(18,2)` and honours `HasPrecision`. Store-type
    aliases and facets such as `INTEGER`, `LONG`, `VARCHAR(n)` and `DECIMAL(p,s)` are recognised.
    Decimal query parameters keep their own precision instead of being rounded to the column's scale.
  - LINQ translation to Databricks SQL. Paging uses `LIMIT`/`OFFSET`, with `Take`/`Skip` values
    inlined as literals. Integer division uses `DIV` (Databricks `/` always returns a fraction).
    String concatenation uses `||`. `CROSS APPLY`/`OUTER APPLY` become `LATERAL` joins.
    `Contains` on a list expands to `IN (:p1, :p2, …)`. Filtering, ordering, aggregates, `GroupBy`,
    joins, `Include` (single and split query), correlated subqueries, per-group `Take`/`First` (via
    `ROW_NUMBER()`) and set operations have SQL baseline tests.
  - .NET member and method translations to Databricks functions:
    - Strings: `Length`, `ToUpper`/`ToLower`, `Trim*`, `Substring` and `IndexOf` (converted to
      1-based positions), `Replace`, `Contains`/`StartsWith`/`EndsWith`,
      `IsNullOrEmpty`/`IsNullOrWhiteSpace`, `Regex.IsMatch` → `rlike`, and `string.Join` over a group
      → `array_join(collect_list(…))`.
    - Math: `Math.Round` → `bround` (half-to-even, as .NET), and `round` for `AwayFromZero`.
      `Ceiling`/`Floor`/`Truncate`, `Pow`/`Sqrt`/`Log`/trigonometry, `Sign`, and `Math.Max`/`Min` →
      `greatest`/`least`.
    - Dates: `DateTime`/`DateTimeOffset`/`DateOnly` parts, `Now`/`UtcNow`/`Today`, and `Add*` →
      `timestampadd`/`date_add`/`add_months`.
    - Conversions: `Convert.To*` and `ToString()` → `CAST`. `Guid.NewGuid()` → `uuid()`.
      `byte[].Length` → `length`.
  - Databricks `EF.Functions` methods: `ILike`, and `DateDiffYear`/`Month`/`Day`/`Hour`/`Minute`/`Second`
    → `timestampdiff`.
  - Model conventions and validation:
    - Integer keys are no longer treated as database-generated. `Guid` keys get version 7
      (time-ordered) values.
    - Model building fails with guidance for configuration Databricks cannot honour: database-generated
      values (`ValueGeneratedOnAdd` without a generator, `HasDefaultValue`/`HasDefaultValueSql`,
      computed columns, `IsRowVersion`, `OwnsMany` shadow keys), sequences, and decimal precision above
      38.
    - Unique indexes log a warning, because Databricks does not enforce them.
  - Unity Catalog three-part names: `modelBuilder.HasDefaultCatalog("main")` and
    `entity.HasCatalog(...)` generate `catalog.schema.table`.
  - `SaveChanges`:
    - `INSERT`/`UPDATE`/`DELETE` run one statement each. Optimistic concurrency uses the affected-row
      count from Databricks' `num_affected_rows` summary.
    - A save of several statements is not atomic. It logs `BricksNetEventId.NonAtomicSaveChanges`, and
      on failure the entities of statements that already committed are marked as saved.
    - `EnableRetryOnFailure()` retries queries, and `SaveChanges` statements individually, on throttling,
      unavailability and Delta concurrent-write conflicts.
    - Consecutive inserts into the same table become one multi-row `INSERT … VALUES (…), (…)`. This is
      capped by `MaxBatchSize` (default 1000 rows; `MaxBatchSize(1)` disables it) and by 256 parameters
      per statement.
  - `EnsureCreated`, `EnsureDeleted`, `CanConnect` and `GenerateCreateScript` for development and
    tests. Tables are created with `CREATE TABLE … USING DELTA`, without constraints or indexes.
    `EnsureDeleted` drops only the model's tables.
  - A live functional test suite (`BricksNet.EntityFrameworkCore.FunctionalTests`). It runs against a
    real warehouse when the `BRICKSNET_EF_*` variables are set, and is skipped otherwise.
  - `ExecuteDelete` and `ExecuteUpdate`, returning the affected-row count. Updates that need other tables
    use `WHERE EXISTS (…)`, because Databricks has no `UPDATE … FROM`. Updates that take values from
    another table throw with guidance to use `MERGE`.
- `BricksNetConnection.KeptSessionIdleTimeout` (`KeptSessionIdleTimeoutSeconds` keyword, default 15
  minutes): a session kept by `KeepSessionAlive` that has been idle longer is replaced on the next open
  rather than reused after the server may have expired it.

### Changed
- **Breaking:** `TIMESTAMP_NTZ` values are returned as `DateTime` (`Kind = Unspecified`) instead of
  `DateTimeOffset`, matching their wall-clock meaning. `TIMESTAMP` is still `DateTimeOffset`.
- **Breaking:** `BricksNetParameter.DbType` is inferred from `Value` until assigned (previously always
  `String`). An explicitly assigned `DbType` is now sent as the parameter's SQL type, so `NULL`s are typed;
  `TypeHint` still takes precedence. `Precision`/`Scale` are stored and used for `DECIMAL` parameters.
- `BricksNetDataReader.GetFieldValue<T>` converts between date/time types, `Guid` (from strings or
  bytes), enums, nullable targets and numeric widths, instead of failing in `Convert.ChangeType`.
  Failures raise `InvalidCastException` naming the column and both types.
- Parameter inference: `byte` is sent as `SMALLINT` and `ushort` as `INT`, since `TINYINT`/`SMALLINT`
  are signed. A `DateTime` sent as `DATE` or `TIMESTAMP_NTZ` is formatted without a time part or
  offset, respectively.

### Fixed
- REST: results were silently truncated to `MaxRows` (default 10,000) because it was sent as `row_limit`.
- REST: only the first result chunk was read; all chunks are now followed via `next_chunk_index`.
- Thrift: only the first `FETCH_NEXT` page was read; pages are now fetched while `hasMoreRows` is set.
- Failed statements now raise `BricksNetException` with the server's message, SQLSTATE and error code
  instead of a generic "Statement failed".
- `ExecuteNonQuery` now waits for the statement to finish and throws on failure; previously it returned
  immediately (REST) or closed—and could abort—the running operation (Thrift).
- Duplicate column names (e.g. `SELECT a.id, b.id`) no longer overwrite each other; repeats are
  suffixed (`id`, `id_1`, …).
- Arrow/CloudFetch result providers are now disposed when replaced or when the operation closes.
- REST cancel now calls `POST /api/2.0/sql/statements/{id}/cancel` (was `DELETE /statements/{id}`).
- `BricksNetDataReader` builds columns from the server schema: empty results expose their columns,
  all-NULL columns report their declared type, and `GetSchemaTable` includes precision and scale.
- OAuth: `OAuthOptions.PersistenceProvider` is now used; the loopback redirect listener binds to
  `localhost` (binding `127.0.0.1` required admin rights on Windows).
- Removed the unreachable inline-Arrow branch in the REST backend (INLINE supports only `JSON_ARRAY`).
- REST: `QueryTimeoutSeconds` is now enforced (client-side deadline, then cancel; `SQLSTATE HYT00`).
  It was previously only used as the initial `wait_timeout`.
- Parameters: values are sent in invariant ISO formats with a SQL type inferred from the CLR type when
  no `TypeHint` is given; Thrift no longer routes `BIGINT`/`DECIMAL` through `double`.
- `DECIMAL(38, s)` values beyond `System.Decimal` range are returned as their exact string instead of
  throwing `OverflowException` (Arrow and JSON results).
- `ProxyOptions.Type = Socks5` now takes effect for `http://` proxy URIs; a SOCKS URI with
  `ProxyType.Http` is rejected.
- REST metadata: `%` in `LIKE` patterns is translated to Databricks' `*` wildcard; backend differences
  are documented.
- Samples: an unknown demo name reports an error instead of silently running `FluentPat`.

[Unreleased]: https://github.com/your-org/bricksnet/compare/HEAD...HEAD
