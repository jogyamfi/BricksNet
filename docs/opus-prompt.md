# Prompt: Generate a Detailed Implementation Plan for `BricksNet` — a .NET 8 Databricks SQL Client Library

---

## Instructions for Claude Opus

You are being asked to produce **two artefacts**:

1. **`bricksnet/PLAN.md`** — A detailed, phased implementation plan for a .NET 8 Databricks SQL client library, structured so that an AI coding agent (GitHub Copilot / Claude) can execute each phase sequentially with minimal ambiguity.
2. **Seven VS Code Agent Skills files** (`*.instructions.md`) in `bricksnet/.github/instructions/` — Encoding .NET best practices relevant to this library so that the coding agent follows them during implementation.

Both artefacts must be **actionable and specific**: every step names the actual classes, interfaces, NuGet packages, and file paths involved. Do not use vague language like "implement the connection logic" — write "create `BricksNetConnection : DbConnection` in `src/BricksNet/Connection/BricksNetConnection.cs`".

The plan will be executed in full by a coding agent across multiple sessions. The skills files will be loaded by that agent throughout implementation to enforce best practices.

---

## Section A — Reference Architectures

You must study the following two reference implementations when designing the plan. They are summarised below; you do not have file system access to them during plan generation, but the implementation agent will.

### A.1 — Java JDBC Driver (`databricks-jdbc/`)

**Overview**: A standards-compliant JDBC driver. Multi-module Maven project. Target users are BI tools, data pipelines, and Java applications.

**Module layout**:
```
databricks-jdbc/
  jdbc-core/               ← All driver source code
  assembly-uber/           ← Fat JAR (all deps bundled)
  assembly-thin/           ← Thin JAR (deps excluded)
  test-assembly-{uber,thin}/  ← Packaging verification tests
```

**Key packages** (`jdbc-core/src/main/java/`):
```
com.databricks.client.jdbc/          → Driver entry point (Driver.java)
com.databricks.client.auth/          → Auth providers
com.databricks.jdbc.api/             → Public JDBC interfaces
com.databricks.jdbc.api.impl/        → Core implementations
com.databricks.jdbc.dbclient/        → Backend client abstraction (IDatabricksClient)
com.databricks.jdbc.dbclient.impl.sqlexec/   → REST/SQL Exec backend
com.databricks.jdbc.dbclient.impl.thrift/    → Thrift binary-protocol backend
com.databricks.jdbc.dbclient.impl.http/      → HTTP transport (DatabricksHttpClient)
com.databricks.jdbc.common/          → Config, constants, URL param parsing
com.databricks.jdbc.telemetry/       → Usage metrics
com.databricks.jdbc.exception/       → Custom exception hierarchy
```

**Core public API** (all implement standard `java.sql.*` interfaces):

| Class | Interface | Purpose |
|---|---|---|
| `Driver` | `java.sql.Driver` | Entry point; parses JDBC URL, creates connections |
| `DatabricksConnection` | `IDatabricksConnection extends java.sql.Connection` | Main connection; owns session lifecycle |
| `DatabricksStatement` | `IDatabricksStatement extends java.sql.Statement` | Sync + async execution (`executeAsync()`, `getExecutionResult()`) |
| `DatabricksPreparedStatement` | `java.sql.PreparedStatement` | Parameterised queries |
| `DatabricksResultSet` | `IDatabricksResultSet extends java.sql.ResultSet` | Streaming/columnar result iteration |
| `DatabricksDatabaseMetaData` | `java.sql.DatabaseMetaData` | Catalog, schema, table, column metadata |
| `DatabricksSession` | internal | Session lifecycle; owns backend client |

**Dual-protocol backend abstraction**:
```
IDatabricksClient  (interface)
  ├── DatabricksSdkClient         → SQL Exec REST API  (SQL Warehouses)
  └── DatabricksThriftServiceClient → TCLIService Thrift (All-purpose clusters)
```
Selection is driven by the `usethriftclient` connection property. The `DatabricksSession` creates the appropriate client via a factory.

**Connection URL format**:
```
jdbc:databricks://<host>:<port>;httpPath=<path>;AuthMech=3;UID=token;PWD=<pat>
jdbc:databricks://<host>:<port>;httpPath=<path>;AuthMech=11;Auth_Flow=0;...
```
Parsed by `DatabricksConnectionContextFactory`. All parameters are typed enums in `DatabricksJdbcUrlParams`.

**Authentication** (`com.databricks.client.auth/`):

| AuthMech | Flow | Key Class |
|---|---|---|
| 3 | Personal Access Token (PAT) | `UID=token; PWD=<token>` via Basic auth |
| 11/Auth_Flow=0 | OAuth token passthrough | `OAuthRefreshCredentialsProvider` |
| 11/PKCE | Authorization code + PKCE | Browser-redirect flow |
| 11/JWT | Service account JWT assertion | `PrivateKeyClientCredentialProvider` |
| Azure MSI | Managed Identity | `AzureMSICredentialProvider` |

**HTTP transport** (`DatabricksHttpClient`):
- Apache HttpComponents 4.5 + async-httpclient 5.5
- Connection pooling with idle timeout
- Rate-limit retry: exponential backoff on HTTP 429
- Transient-error retry on HTTP 503
- Configurable proxy: HTTP, SOCKS, system proxy, per-CloudFetch proxy
- Custom request headers via `http.header.*` params

**Result handling**:
- Primary format: Apache Arrow IPC columnar batches
- Fallback: inline JSON columnar rows
- CloudFetch: parallel multi-threaded S3/GCS/ADLS downloads (default 16 threads)
- LZ4 compression on result payloads (configurable)
- Direct results: first batch embedded in execute response to eliminate a round-trip

**Test structure**:
- Unit tests: JUnit 5 + Mockito; ~352 test classes
- Fake service (record/replay): `src/test/java/.../fakeservice/` — intercepts HTTP at transport level; RECORD mode captures real server responses; REPLAY mode serves them deterministically; DRY mode passes through
- SQL Logic Tests: standard SQL conformance suite
- E2E integration tests: require live Databricks workspace

**Key config parameters**:
```
EnableArrow, EnableDirectResults, QueryResultCompressionType,
cloudFetchThreadPoolSize, asyncexecpollinterval,
RateLimitRetry, RateLimitRetryTimeout, TemporarilyUnavailableRetry,
ssl, AllowSelfSignedCerts, SSLTrustStore, UseSystemTrustStore
```

---

### A.2 — Node.js Driver (`databricks-sql-nodejs/`)

**Overview**: TypeScript library exposing a modern async API. Not JDBC — design is closer to what the .NET library's fluent API should look like. Also has a second native-code "kernel" backend (not in scope for .NET).

**Entry point and public API** (`lib/index.ts`):
```typescript
DBSQLClient          // connect() → opens transport; openSession() → DBSQLSession
DBSQLSession         // executeStatement(), getCatalogs(), getSchemas(), getTables(), getColumns(), close()
DBSQLOperation       // fetchChunk(), fetchAll(), status(), cancel(), close()
DBSQLParameter       // named and positional parameter binding
DBSQLLogger          // configurable log levels
```

**Contracts layer** (`lib/contracts/`):
```typescript
IDBSQLClient         // ConnectionOptions, connect(), openSession()
IDBSQLSession        // ExecuteStatementOptions, StatementOptions
IOperation           // fetchChunk(options), fetchAll(options), status(), cancel(), close()
IBackend             // openSession(opts): ISessionBackend
ISessionBackend      // executeStatement(), getCatalogs(), …, closeSession()
IOperationBackend    // getOperationStatus(), fetchResults(), cancelOperation(), closeOperation()
IClientContext       // shared config (ClientConfig) passed through all layers
```

**Backend abstraction** (two implementations of `IBackend`):
```
IBackend
  ├── ThriftBackend   (lib/thrift-backend/)   → Thrift RPC over HTTP
  └── KernelBackend   (lib/kernel/)           → Native napi binding (NOT in scope for .NET)
```

**Thrift transport** (`lib/connection/connections/`):
- `HttpConnection` → raw HTTP transport
- `ThriftHttpConnection` → Thrift multiplexer over HTTP
- `HttpRetryPolicy` → exponential backoff with jitter; configurable `retryMaxAttempts`, `retriesTimeout`, `retryDelayMin/Max`
- `NullRetryPolicy` → no-op for testing

**Authentication** (`lib/connection/auth/`):
```
IAuthentication            ← interface; getToken(): string
PlainHttpAuthentication    ← PAT (Personal Access Token) via Basic auth
DatabricksOAuth/           ← OAuth2 flows
  OAuthManager             ← orchestration
  AuthorizationCode        ← PKCE flow
  OAuthPersistence         ← token cache interface
tokenProvider/
  ITokenProvider           ← pluggable provider interface
  CachedTokenProvider      ← wraps any provider with TTL cache
  FederationProvider       ← token federation chain
  StaticTokenProvider, ExternalTokenProvider
```

**Result handling** (`lib/result/`):

| Handler | Format | When Used |
|---|---|---|
| `ArrowResultHandler` | Apache Arrow IPC | Default for SQL Warehouses |
| `JsonResultHandler` | JSON columnar | Fallback / legacy |
| `CloudFetchResultHandler` | Pre-signed URLs → parallel download | Large result sets |
| `ResultSlicer` | Windowing over handlers | `maxRows` enforcement |

**Connection options** (key fields from `IDBSQLClient.ts`):
```typescript
host, path, port, socketTimeout, proxy,
authType, token, credentials,
directResultsDefaultMaxRows, fetchChunkDefaultMaxRows,
useCloudFetch, cloudFetchConcurrentDownloads, useLZ4Compression,
preserveBigNumericPrecision,
retryMaxAttempts, retriesTimeout, retryDelayMin, retryDelayMax,
telemetryEnabled, telemetryBatchSize, telemetryFlushIntervalMs
```

**Session options** (per-statement, from `IDBSQLSession.ts`):
```typescript
queryTimeout, maxRows, queryTags (key-value string map),
statementConf (Spark config overlay)
```

---

## Section B — Goal & Requirements

### B.1 What to build

A .NET 8 Databricks SQL client library, NuGet-publishable as **`BricksNet`**, that provides:

1. **Modern fluent API** — mirrors the Node.js `DBSQLClient → DBSQLSession → DBSQLOperation` pattern, adapted for C# async/await idioms.
2. **ADO.NET provider** — a full `System.Data` / `System.Data.Common` compliant driver (`DbConnection`, `DbCommand`, `DbDataReader`, etc.) that wraps the fluent API, enabling compatibility with Dapper, Entity Framework Core (read queries), and other ORMs.

### B.2 Project structure (target layout in `bricksnet/`)

```
bricksnet/
  BricksNet.sln
  src/
    BricksNet/                         ← Main library project (net8.0)
      Connection/
      Auth/
      Backends/
        Rest/
        Thrift/
      Results/
      Contracts/
      AdoNet/
      Telemetry/
      Utils/
      BricksNet.csproj
  tests/
    BricksNet.Tests/                   ← Unit tests (xUnit, Moq)
      BricksNet.Tests.csproj
    BricksNet.IntegrationTests/        ← Fake-service replay + live E2E
      FakeService/
      E2E/
      BricksNet.IntegrationTests.csproj
  .github/
    instructions/                      ← Skills files (generated in Section D)
  docs/
    opus-prompt.md                     ← This file
```

### B.3 Functional requirements

| Requirement | Detail |
|---|---|
| Target framework | `net8.0` only |
| Backends | REST (SQL Exec API) and Thrift (TCLIService), selectable via `UseThrift` option |
| Auth | Personal Access Token (PAT) and OAuth2 Authorization Code + PKCE |
| Result formats | Apache Arrow IPC (primary), JSON columnar (fallback), CloudFetch parallel download |
| Compression | LZ4 decompression on result payloads (using `K4os.Compression.LZ4`) |
| Direct results | First result batch embedded in execute response (eliminate round-trip) |
| Retry | Exponential backoff with jitter on HTTP 429 and 503 |
| Proxy | HTTP and SOCKS5 proxy support |
| Metadata | `GetCatalogs`, `GetSchemas`, `GetTables`, `GetColumns` (both fluent and ADO.NET `GetSchema`) |
| Cancellation | `CancellationToken` accepted on all async methods |
| Logging | `Microsoft.Extensions.Logging.ILogger<T>` throughout; no direct Console writes |
| Telemetry | Structured events via `System.Diagnostics.ActivitySource` (OTel-compatible) |
| Packaging | Single NuGet package `BricksNet`; SymbolPackage for debugging |

### B.4 Non-requirements (explicitly out of scope)

- Native / kernel backend (Node.js `KernelBackend` equivalent)
- Azure MSI, JWT service principal auth (defer to a later release)
- Write-back / DML (INSERT, UPDATE, DELETE) — the library executes arbitrary SQL but does not add higher-level DML abstractions
- Connection pooling (rely on `HttpClientFactory` and caller-managed pooling)
- Entity Framework Core provider (a separate package, out of scope)
- .NET Standard 2.0 / .NET Framework compatibility

---

## Section C — Requested Output 1: `bricksnet/PLAN.md`

Produce a Markdown file at `bricksnet/PLAN.md` structured as follows.

### Top-level structure

```markdown
# BricksNet — .NET 8 Implementation Plan

## Overview
[brief description, goals, guiding principles]

## Dependency Graph
[Mermaid diagram showing phase dependencies]

## Phase 1: ...
### Goal
### Input Artefacts
### Output Artefacts
### Implementation Steps
### Key Classes & Interfaces
### NuGet Dependencies
### .NET-Specific Considerations
### Verification
---

## Phase 2: ...
[same structure]
...
```

### Required phases (12 total)

---

#### Phase 1 — Solution Scaffolding & Project Structure

**Goal**: Create the `bricksnet/` solution with all projects, global configuration, and CI skeleton. The agent can run `dotnet build` successfully at the end.

**Must cover**:
- `BricksNet.sln` creation with all three projects
- `Directory.Build.props` at solution root for shared properties: `Nullable=enable`, `TreatWarningsAsErrors=true`, `ImplicitUsings=enable`, `LangVersion=latest`, `AnalysisLevel=latest`
- `Directory.Packages.props` for centralised NuGet version management
- `global.json` pinning .NET 8 SDK
- `.editorconfig` (C# conventions)
- `.gitignore` for .NET projects
- `BricksNet.csproj` with `net8.0`, `Nullable=enable`, package metadata stubs, `GenerateDocumentationFile=true`
- `BricksNet.Tests.csproj` with xUnit 2.9+, `coverlet.collector`, `Microsoft.NET.Test.Sdk`
- `BricksNet.IntegrationTests.csproj`
- GitHub Actions workflow skeleton (`.github/workflows/build.yml`): restore → build → test (unit only)

---

#### Phase 2 — Core Contracts & Interfaces

**Goal**: Define all public and internal interfaces that other phases implement against. No implementation code in this phase.

**Must cover**:

*Fluent API contracts* (`src/BricksNet/Contracts/`):
- `IBricksNetClient` — `ConnectAsync()`, `OpenSessionAsync(SessionOptions?)`, `CloseAsync()`, `IAsyncDisposable`
- `IBricksNetSession` — `ExecuteStatementAsync(string sql, StatementOptions?)`, `GetCatalogsAsync()`, `GetSchemasAsync()`, `GetTablesAsync()`, `GetColumnsAsync()`, `CloseAsync()`, `IAsyncDisposable`
- `IBricksNetOperation` — `FetchAllAsync<T>()`, `FetchChunkAsync<T>(int maxRows)`, `GetStatusAsync()`, `CancelAsync()`, `CloseAsync()`, `IAsyncDisposable`
- `IResultsProvider` — `HasMoreRows`, `FetchNextChunkAsync()` returning `IReadOnlyList<IReadOnlyDictionary<string, object?>>`
- `IBackend` — `OpenSessionAsync(BackendSessionOptions)` returning `ISessionBackend`
- `ISessionBackend` — `ExecuteStatementAsync(...)`, `GetCatalogsAsync(...)`, etc., returning `IOperationBackend`
- `IOperationBackend` — `GetStatusAsync()`, `FetchResultsAsync(FetchOptions)`, `CancelAsync()`, `CloseAsync()`
- `IAuthProvider` — `GetTokenAsync(CancellationToken)` returning `string`
- `ITokenCache` — `TryGetTokenAsync(...)`, `StoreTokenAsync(...)`

*Configuration DTOs* (immutable records):
- `BricksNetClientOptions` — `Host`, `Port`, `HttpPath`, `AuthType`, `UseThrift`, `RetryOptions`, `ProxyOptions`, `SslOptions`, `TelemetryOptions`
- `SessionOptions` — `Catalog`, `Schema`, `QueryTimeoutSeconds`, `MaxRows`, `QueryTags`
- `StatementOptions` — inherits `SessionOptions`, adds `Parameters`
- `FetchOptions` — `MaxRows`, `CancellationToken`
- `RetryOptions` — `MaxAttempts`, `TimeoutMs`, `DelayMinMs`, `DelayMaxMs`
- `ProxyOptions` — `Uri`, `Type` (Http/Socks5), `Credentials`

*ADO.NET contracts* (`src/BricksNet/AdoNet/`):
- `BricksNetConnectionStringBuilder : DbConnectionStringBuilder` — typed properties for all `BricksNetClientOptions` fields

*Result metadata*:
- `ColumnInfo` record — `Name`, `TypeName`, `Ordinal`, `IsNullable`, `Precision`, `Scale`
- `ResultFormat` enum — `Arrow`, `JsonColumnar`, `CloudFetch`
- `QueryStatus` enum — `Running`, `Succeeded`, `Failed`, `Cancelled`, `Closed`, `Pending`

---

#### Phase 3 — HTTP Transport Layer

**Goal**: Build a robust HTTP client wrapper that all backends use.

**Must cover**:
- `BricksNetHttpClient` class (`src/BricksNet/Connection/BricksNetHttpClient.cs`) using `IHttpClientFactory` for lifecycle management
- `BricksNetHttpClientOptions` — base URL, default headers (`User-Agent: BricksNet.dotnet/<version>`), timeout
- `HttpRetryHandler : DelegatingHandler` — implements exponential backoff with full jitter on HTTP 429 and 503; respects `Retry-After` header; honours `RetryOptions`; logs each retry at `Debug` level
- `ProxyConfigHandler : DelegatingHandler` — configures `HttpClientHandler.Proxy` from `ProxyOptions`; supports `WebProxy` (HTTP) and custom `IWebProxy` for SOCKS5 (using `SOCKS5Proxy` from `Yarp.ReverseProxy` or `SocksSharp` NuGet)
- `AuthHeaderHandler : DelegatingHandler` — calls `IAuthProvider.GetTokenAsync()` and injects `Authorization: Bearer <token>` header
- `HttpClientBuilderExtensions` — `AddBricksNetHttpClient(this IServiceCollection, BricksNetClientOptions)` for DI registration; chains the three handlers in order: Proxy → Auth → Retry
- SSL configuration: accept system trust store by default; option to supply custom `X509Certificate2Collection`; **never** disable certificate validation in production code (log a warning if `AllowSelfSignedCerts` is true)
- `HttpContentExtensions` — `ReadAsArrowAsync()`, `ReadAsJsonColumnarAsync()` extension methods

**NuGet packages**:
- `Microsoft.Extensions.Http` (IHttpClientFactory)
- `Microsoft.Extensions.DependencyInjection.Abstractions`

---

#### Phase 4 — Authentication Providers

**Goal**: Implement PAT and OAuth2 PKCE auth; both implement `IAuthProvider`. *(Depends on Phase 3 HTTP transport.)*

**Must cover**:

*PAT provider* (`src/BricksNet/Auth/PatAuthProvider.cs`):
- Constructor takes `string token`
- `GetTokenAsync` returns the token immediately (no network call)
- Token must not appear in logs (use `***` in any log output referencing the token)

*OAuth2 PKCE provider* (`src/BricksNet/Auth/OAuthPkceAuthProvider.cs`):
- Implements Authorization Code flow with PKCE (RFC 7636)
- `GeneratePkceChallenge()` → `code_verifier` (random 96 bytes, base64url) + `code_challenge` (SHA256 of verifier, base64url)
- Opens system browser via `Process.Start` to authorization URL
- Starts a local `HttpListener` on a loopback port to receive the redirect callback
- Exchanges authorization code for token via HTTP POST (uses `BricksNetHttpClient`)
- `OAuthTokenCache : ITokenCache` — in-memory LRU cache with TTL; extends to file-based cache via `IOAuthPersistence` interface
- `CachedTokenProvider` — wraps any `IAuthProvider`; checks cache before fetching; refreshes when TTL < 30 seconds remaining
- **Security**: never log token values; validate `state` parameter to prevent CSRF; use `SecureRandom` (i.e., `RandomNumberGenerator`) for all random bytes

*OAuth configuration DTOs*:
- `OAuthOptions` — `ClientId`, `AuthorizationEndpoint`, `TokenEndpoint`, `Scopes`, `RedirectPort`, `PersistenceProvider`

*Factory method*:
- `AuthProviderFactory.Create(BricksNetClientOptions)` — returns the correct `IAuthProvider` based on `AuthType`

---

#### Phase 5 — Thrift Code Generation & Transport

**Goal**: Generate C# Thrift types from `TCLIService.thrift` and build the Thrift HTTP transport. *(Depends on Phase 3.)*

**Must cover**:
- Copy/reference `TCLIService.thrift` from `databricks-jdbc/src/` or `databricks-sql-nodejs/thrift/` as the IDL source
- Use the `Apache.Thrift` NuGet package (not custom codegen); or if codegen is needed, document the exact `thrift` CLI command and output path (`src/BricksNet/Backends/Thrift/Generated/`)
- `ThriftHttpTransport` class — wraps `BricksNetHttpClient`; implements `TTransport`; sends binary Thrift frames via HTTP POST to `<host><httpPath>`; reads response body as binary Thrift frame
- `ThriftSessionBackend : ISessionBackend` — maps `ISessionBackend` calls to `TCLIService.Client` Thrift calls:
  - `OpenSessionAsync` → `TOpenSessionReq` / `TOpenSessionResp`
  - `ExecuteStatementAsync` → `TExecuteStatementReq` / `TExecuteStatementResp`
  - `GetOperationStatusAsync` → `TGetOperationStatusReq` / `TGetOperationStatusResp`
  - `FetchResultsAsync` → `TFetchResultsReq` / `TFetchResultsResp`
  - `CancelOperationAsync` → `TCancelOperationReq`
  - `CloseOperationAsync` → `TCloseOperationReq`
  - `CloseSessionAsync` → `TCloseSessionReq`
- `ThriftOperationBackend : IOperationBackend`
- `ThriftBackend : IBackend` — factory; owns `TCLIService.Client` lifecycle
- Thrift result extraction: parse `TRowSet` from `TFetchResultsResp`; identify format from `TRowSet.columns` (JSON) vs `TRowSet.arrowBatches` (Arrow) vs `TRowSet.resultLinks` (CloudFetch)
- Status checking helper: `ThriftStatusChecker.EnsureSuccess(TStatus)` — throws `BricksNetException` on error status codes

**NuGet packages**:
- `Apache.Thrift` (0.21+)

---

#### Phase 6 — REST / SQL Exec Backend

**Goal**: Implement the SQL Exec REST API backend. *(Depends on Phase 3, Phase 4.)*

**Reference**: Databricks SQL Exec API — documented at `https://docs.databricks.com/api/workspace/statementexecution`. Key endpoints:
- `POST /api/2.0/sql/statements` — execute statement
- `GET  /api/2.0/sql/statements/{statement_id}` — poll status
- `GET  /api/2.0/sql/statements/{statement_id}/result/chunks/{chunk_index}` — fetch chunk
- `DELETE /api/2.0/sql/statements/{statement_id}` — cancel

**Must cover**:
- `RestBackend : IBackend` — creates `RestSessionBackend`
- `RestSessionBackend : ISessionBackend`
- `RestOperationBackend : IOperationBackend`
- Request/response DTOs in `src/BricksNet/Backends/Rest/Dto/` (use `System.Text.Json` with `JsonSerializerOptions` configured for snake_case)
  - `ExecuteStatementRequest`, `ExecuteStatementResponse`
  - `StatementStatus`, `StatementState` enum
  - `ResultManifest`, `ResultChunkIndex`, `ExternalLink`
  - `StatementParameterDto` (named parameters)
- `RestStatementPoller` — polls `GET /statements/{id}` at configurable intervals; implements backoff (start 200ms, max 10s); honours `CancellationToken`; throws `OperationCanceledException` on cancellation
- Inline results: if `ExecuteStatementResponse.result` is non-null (direct results), skip polling and use the embedded chunk
- Direct results request: always send `disposition=INLINE` and `format=ARROW_STREAM` in the execute request body
- `SqlExecHttpClient` — thin wrapper over `BricksNetHttpClient` adding path prefix and JSON serialization helpers

**NuGet packages**:
- `System.Text.Json` (built-in with .NET 8)

---

#### Phase 7 — Result Handling

**Goal**: Parse result payloads into .NET objects. *(Depends on Phase 5 for Thrift result extraction; Phase 6 for REST result extraction.)*

**Must cover**:

*Arrow result handler* (`src/BricksNet/Results/ArrowResultHandler.cs`):
- Reads Apache Arrow IPC stream format from `byte[]` or `Stream`
- Uses `Apache.Arrow.Ipc.ArrowStreamReader` to iterate `RecordBatch` objects
- Converts each `RecordBatch` to `IReadOnlyList<IReadOnlyDictionary<string, object?>>` via `ArrowTypeConverter`
- `ArrowTypeConverter` — static mapping table: `Int8/16/32/64` → `sbyte/short/int/long`, `UInt8/16/32/64` → `byte/ushort/uint/ulong`, `Float` → `float`, `Double` → `double`, `Utf8` → `string`, `LargeUtf8` → `string`, `Date32/Date64` → `DateOnly`, `Timestamp` → `DateTimeOffset`, `Decimal128` → `decimal`, `Boolean` → `bool`, `Binary/LargeBinary` → `byte[]`, `List` → `object[]`, `Struct` → `Dictionary<string,object?>`

*JSON columnar result handler* (`src/BricksNet/Results/JsonResultHandler.cs`):
- Parses Thrift `TRowSet.columns` (columnar JSON arrays)
- Re-assembles into row-oriented `IReadOnlyDictionary<string, object?>`
- Applies type coercion using `ColumnInfo` metadata

*CloudFetch result handler* (`src/BricksNet/Results/CloudFetchResultHandler.cs`):
- Receives `IEnumerable<ExternalLink>` (pre-signed URLs with expiry)
- Downloads URLs in parallel using `Parallel.ForEachAsync` with configurable `MaxDegreeOfParallelism` (from `cloudFetchConcurrentDownloads`, default 4)
- Each download: `HttpClient.GetStreamAsync()` → optionally LZ4-decompress → parse as Arrow IPC
- Validates URL expiry before download; throws `CloudFetchLinkExpiredException` if expired
- Downloads are ordered by `chunk_index` before returning

*LZ4 decompression* (`src/BricksNet/Results/Lz4Decompressor.cs`):
- Wraps `K4os.Compression.LZ4.Streams.LZ4Stream.Decode()` to decompress a `Stream`

*Result slicer / windowing* (`src/BricksNet/Results/ResultSlicer.cs`):
- Wraps any `IResultsProvider`; enforces `maxRows` across multiple chunks

*Central factory* (`src/BricksNet/Results/ResultHandlerFactory.cs`):
- `Create(ResultFormat, ColumnInfo[], options)` → returns appropriate `IResultsProvider`

**NuGet packages**:
- `Apache.Arrow` (18+)
- `K4os.Compression.LZ4` (1.3+)

---

#### Phase 8 — ADO.NET Adapter Layer

**Goal**: Wrap the fluent API in standard `System.Data.Common` types. *(Depends on Phases 2, 6, 7.)*

**Must cover**:
- `BricksNetConnection : DbConnection`
  - Constructor: `(string connectionString)` and `(BricksNetClientOptions options)`
  - `Open()` / `OpenAsync()` — creates `IBricksNetClient`, connects, opens session; transitions state machine `Closed → Connecting → Open`
  - `Close()` / `CloseAsync()` — closes session and client; transitions to `Closed`
  - `CreateCommand()` → `BricksNetCommand`
  - `ChangeDatabase(string)` — sets `Schema` on the session options
  - `BeginDbTransaction()` — throws `NotSupportedException` (Databricks SQL does not support multi-statement transactions)
  - `ServerVersion` — returns version string from server info
- `BricksNetCommand : DbCommand`
  - `CommandText` (SQL string)
  - `CommandTimeout` → maps to `StatementOptions.QueryTimeoutSeconds`
  - `Parameters` → `BricksNetParameterCollection`
  - `ExecuteDbDataReader()` / `ExecuteDbDataReaderAsync()` → executes statement, returns `BricksNetDataReader`
  - `ExecuteNonQuery()` / `ExecuteNonQueryAsync()` → executes, discards results, returns -1
  - `ExecuteScalar()` / `ExecuteScalarAsync()` → returns first column of first row
  - `Cancel()` → calls `IBricksNetOperation.CancelAsync().GetAwaiter().GetResult()` (document the deadlock caveat)
- `BricksNetDataReader : DbDataReader`
  - Implements `Read()` / `ReadAsync()` — advances through rows
  - Implements all `GetXxx(int ordinal)` methods using `ArrowTypeConverter` for type coercion
  - `GetSchemaTable()` — returns `DataTable` with standard ADO.NET column schema
  - `RecordsAffected` always returns -1
  - Implements `IAsyncEnumerable<IDataRecord>` (bonus: enables `await foreach` iteration)
- `BricksNetParameter : DbParameter`
  - `ParameterName`, `Value`, `DbType`, `Direction` (only `Input` supported)
  - `BricksNetParameterCollection : DbParameterCollection`
- `BricksNetException : DbException`
  - `SqlState` (string) — maps from server error codes
  - `ErrorCode` (int) — HTTP status or Thrift status code
  - `QueryId` (string?) — statement ID for diagnostics
- `BricksNetConnectionStringBuilder : DbConnectionStringBuilder`
  - Typed properties: `Host`, `Port`, `HttpPath`, `AuthType`, `Token`, `Catalog`, `Schema`, `UseThrift`, `QueryTimeout`, `RetryMaxAttempts`, plus all proxy and SSL options
  - Round-trips through `ConnectionString` string

---

#### Phase 9 — Telemetry & Structured Logging

**Goal**: Add non-invasive observability. *(Can be implemented in parallel with Phases 5–8.)*

**Must cover**:
- `BricksNetActivitySource` static class — `ActivitySource` named `"BricksNet"`; creates spans for: connection open/close, statement execute, result fetch, auth token fetch
- Span attributes (following OpenTelemetry DB semantic conventions):
  - `db.system = "databricks"`
  - `db.statement` (SQL text, truncated to 1000 chars)
  - `db.name` (catalog.schema)
  - `server.address`, `server.port`
  - `databricks.statement_id`
  - `databricks.backend` (`rest` or `thrift`)
- `ILogger<T>` usage guidelines (in code comments):
  - `LogTrace` — per-row or per-chunk events (very high volume)
  - `LogDebug` — per-statement lifecycle, retry attempts
  - `LogInformation` — connection open/close, session start/end
  - `LogWarning` — retries exhausted but succeeded, deprecated options
  - `LogError` — unrecoverable errors
  - Never log token values or full SQL at `LogInformation` or above
- `TelemetryOptions` record — `Enabled`, `DiagnosticListenerName`, `SensitiveDataLoggingEnabled` (default false)
- `LogMessages` static class — compile-time log message definitions using `[LoggerMessage]` source generator

---

#### Phase 10 — Fluent API Implementation (BricksNetClient)

**Goal**: Implement the top-level fluent API that ties all previous phases together. *(Depends on Phases 2–9.)*

**Must cover**:
- `BricksNetClient : IBricksNetClient, IAsyncDisposable`
  - Constructor: `(BricksNetClientOptions options, ILogger<BricksNetClient>? logger = null)`
  - DI-friendly constructor: `(BricksNetClientOptions options, IHttpClientFactory httpClientFactory, ILogger<BricksNetClient>? logger = null)`
  - `ConnectAsync()` — validates options; creates `IBackend` (via `BackendFactory`); stores it
  - `OpenSessionAsync(SessionOptions?)` → creates `BricksNetSession`
  - `DisposeAsync()` — closes all open sessions; disposes `IBackend`
- `BricksNetSession : IBricksNetSession, IAsyncDisposable`
  - `ExecuteStatementAsync(string sql, StatementOptions?)` → calls `ISessionBackend.ExecuteStatementAsync()`; wraps result in `BricksNetOperation`
  - Metadata methods: `GetCatalogsAsync()`, `GetSchemasAsync()`, `GetTablesAsync()`, `GetColumnsAsync()`
  - `DisposeAsync()` — cancels in-flight operations; closes session via `ISessionBackend.CloseSessionAsync()`
- `BricksNetOperation : IBricksNetOperation, IAsyncDisposable`
  - `FetchAllAsync<T>()` — accumulates all chunks; deserialises to `T` via `JsonSerializer` (when `T` is a POCO) or returns `IReadOnlyList<IReadOnlyDictionary<string,object?>>` (when `T` is `IDictionary`)
  - `FetchChunkAsync<T>(int maxRows)` — fetches next chunk only
  - `GetStatusAsync()` → `QueryStatus`
  - `CancelAsync()` → `IOperationBackend.CancelAsync()`
  - `DisposeAsync()` → closes operation on server
- `BackendFactory` — `Create(BricksNetClientOptions, IHttpClientFactory, ILogger)` → `IBackend` (`RestBackend` or `ThriftBackend` based on `UseThrift`)
- `ServiceCollectionExtensions` — `AddBricksNet(this IServiceCollection, Action<BricksNetClientOptions>)` for ASP.NET Core DI integration; registers `IHttpClientFactory`, `IBricksNetClient`, `BricksNetClientOptions`

---

#### Phase 11 — Test Harness

**Goal**: Build a comprehensive test suite mirroring the Java driver's fake-service pattern. *(Runs in parallel with Phases 3–10; grows incrementally.)*

**Unit tests** (`tests/BricksNet.Tests/`):
- `HttpRetryHandlerTests` — verify backoff timing and retry count using `MockHttpMessageHandler` (via `RichardSzalay.MockHttp`)
- `AuthHeaderHandlerTests` — verify `Authorization` header injection
- `PatAuthProviderTests` — verify token returned, not logged
- `OAuthPkceAuthProviderTests` — mock `HttpListener`; verify PKCE challenge/verifier math; verify CSRF state check
- `ArrowTypeConverterTests` — parameterised tests for all 18 Arrow → .NET type mappings
- `ArrowResultHandlerTests` — round-trip tests: build `RecordBatch`, serialise, deserialise through handler, compare
- `JsonResultHandlerTests` — columnar JSON → row dicts
- `CloudFetchResultHandlerTests` — mock HTTP downloads; verify ordering; verify expiry check
- `BricksNetConnectionStringBuilderTests` — round-trip all properties through `ConnectionString`
- `BackendFactoryTests` — verify correct backend selected based on `UseThrift`
- `ResultSlicerTests` — verify maxRows enforcement across chunk boundaries

**Fake service** (`tests/BricksNet.IntegrationTests/FakeService/`):
- `FakeServiceMode` enum — `Record`, `Replay`, `Passthrough`
- `FakeHttpServer` — `Microsoft.AspNetCore.TestHost`-based in-process HTTP server
- `FakeServiceRecorder` — in `Record` mode, forwards requests to real Databricks workspace and saves request/response pairs as JSON fixtures in `fixtures/`
- `FakeServiceReplayer` — in `Replay` mode, matches incoming requests to fixture files and returns stored responses; matching strategy: HTTP method + path (ignore query params order) + normalized request body
- Fixture directory structure: `fixtures/{service_type}/{test_name}/step-{n}.json` where each file contains `{ "request": {...}, "response": {...} }`
- `FakeServiceFixture : IAsyncLifetime` — xUnit fixture that starts `FakeHttpServer` before tests and tears it down after
- `IntegrationTestBase` — base class for all integration tests; injects `FakeServiceFixture`; provides `CreateClient(FakeServiceMode)` helper

**Integration test suites**:
- `RestBackendIntegrationTests` — covers execute/poll/fetch against fake REST service
- `ThriftBackendIntegrationTests` — covers Thrift execute/fetch using fake service
- `CloudFetchIntegrationTests` — verify parallel downloads with fake pre-signed URLs
- `AdoNetIntegrationTests` — `DbConnection`/`DbCommand`/`DbDataReader` against fake service

**Test data / fixtures**:
- `fixtures/rest/execute_select/` — sample execute + poll + fetch response
- `fixtures/rest/execute_direct_results/` — sample response with inline result
- `fixtures/thrift/execute_select/` — sample Thrift binary frames (base64-encoded in JSON)

---

#### Phase 12 — NuGet Packaging & CI

**Goal**: Make the library publishable and CI green. *(Depends on all prior phases.)*

**Must cover**:
- `BricksNet.csproj` packaging properties:
  ```xml
  <PackageId>BricksNet</PackageId>
  <Version>0.1.0-alpha</Version>
  <Authors>Your Name</Authors>
  <Description>BricksNet — Databricks SQL client library for .NET 8</Description>
  <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageProjectUrl>https://github.com/...</PackageProjectUrl>
  <RepositoryUrl>https://github.com/...</RepositoryUrl>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <Deterministic>true</Deterministic>
  <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  ```
- `README.md` at solution root with: quick-start code snippet (both fluent API and ADO.NET), connection string format, auth examples, NuGet badge placeholder
- GitHub Actions workflow (`.github/workflows/build.yml`):
  - Trigger: push to `main`, PR to `main`
  - Steps: checkout → setup .NET 8 → restore → build → unit test with coverage → pack (on `main` only)
  - Coverage: `dotnet test --collect:"XPlat Code Coverage"` → upload to Codecov
- Security scan workflow (`.github/workflows/security.yml`):
  - `dotnet list package --vulnerable` check
  - OWASP Dependency Check (via GitHub Action `dependency-check/Dependency-Check_Action`)
- Changelog: `CHANGELOG.md` with Keep-a-Changelog format; initial `[Unreleased]` section

---

## Section D — Requested Output 2: Seven Skills Files

For each file below, generate the **full file content** ready to be saved to `bricksnet/.github/instructions/<filename>`. Each file must use the VS Code `.instructions.md` format with a YAML frontmatter block, followed by actionable rules in Markdown.

The frontmatter format is:
```yaml
---
applyTo: "<glob pattern matching files where this skill applies>"
---
```

Use specific globs (e.g., `**/src/BricksNet/**/*.cs` rather than `**/*.cs`) so skills are scoped to relevant files only.

Rules must be imperative and specific ("Always use `ConfigureAwait(false)`") not vague ("Consider using ConfigureAwait"). Include concrete code examples for non-obvious rules.

---

### D.1 — `dotnet-library-conventions.instructions.md`

**applyTo**: `**/src/BricksNet/**/*.cs`

Content must cover (minimum, expand as needed):
- All public async methods must return `Task<T>` or `ValueTask<T>`; never `async void` except event handlers
- Every `await` call in a library (non-UI) context must use `.ConfigureAwait(false)`
- Every public async method must accept `CancellationToken cancellationToken = default` as its last parameter and pass it through to all awaited calls
- Classes that own `IDisposable` resources must implement both `IDisposable` and `IAsyncDisposable`; the sync `Dispose()` must call `DisposeAsync().AsTask().GetAwaiter().GetResult()` only as a last resort and must document the caveat
- `record` types (immutable) for all configuration and data-transfer objects; use `init`-only properties
- Enable nullable reference types (`#nullable enable` or project-wide); all public APIs must be nullable-annotated
- Use `ArgumentNullException.ThrowIfNull()` and `ArgumentException.ThrowIfNullOrEmpty()` for public API parameter validation
- Use `[LoggerMessage]` source-generated log methods; never call `_logger.Log(...)` with string interpolation (prevents boxing allocations on disabled log levels)
- Do not expose `List<T>` or `Dictionary<K,V>` in public APIs; use `IReadOnlyList<T>` and `IReadOnlyDictionary<K,V>`
- Do not use `Thread.Sleep`; use `Task.Delay` with `CancellationToken`
- Exception messages must not contain sensitive data (tokens, credentials, full SQL at ERROR level)

---

### D.2 — `adonet-provider-patterns.instructions.md`

**applyTo**: `**/src/BricksNet/AdoNet/**/*.cs`

Content must cover:
- Inherit from the abstract `System.Data.Common` base classes (`DbConnection`, `DbCommand`, `DbDataReader`, etc.), not the interfaces (`IDbConnection`, etc.); the abstract base classes provide default implementations and future-proof the implementation
- `DbConnection.State` must be kept accurate at all times using the `ConnectionState` enum transitions: `Closed → Connecting → Open → Closed`; never leave state stale after exceptions
- All ADO.NET `Open()`/`Close()` sync methods must internally call the `Async` variant using `GetAwaiter().GetResult()` and document that callers should use the async variants to avoid deadlocks
- `DbCommand.ExecuteReader()` must return `DbDataReader` (not `DataReader`); override `ExecuteDbDataReader` not `ExecuteReader`
- `BricksNetException` must inherit `DbException` and set `DbException.SqlState` and `DbException.ErrorCode`
- `GetSchemaTable()` on `DbDataReader` must return a `DataTable` with the standard 24 column-schema columns defined by `System.Data.Common.SchemaTableColumn`
- `BricksNetConnectionStringBuilder` must use `DbConnectionStringBuilder.this[string keyword]` indexer internally; all typed properties must call the base indexer
- `DbParameter.Direction` must only accept `ParameterDirection.Input`; throw `NotSupportedException` for `Output`, `InputOutput`, `ReturnValue`
- Connection string keyword aliases: `Host`/`Server`/`Data Source` must all map to the same option; implement `ContainsKey` and `TryGetValue` overrides

---

### D.3 — `thrift-dotnet.instructions.md`

**applyTo**: `**/src/BricksNet/Backends/Thrift/**/*.cs`

Content must cover:
- Use `Apache.Thrift` NuGet package; import namespace `Thrift.Protocol`, `Thrift.Transport`
- All Thrift RPC calls must use the async overloads (`OpenSessionAsync`, `ExecuteStatementAsync`, etc.) with `CancellationToken` propagation
- The `TBinaryProtocol` framing: create `TBinaryProtocol` over a `TStreamTransport` wrapping the raw HTTP request/response body; read the full response body into a `MemoryStream` before creating the protocol to avoid stream-position issues with `HttpContent.ReadAsStreamAsync`
- Thrift `TOperationHandle` is opaque; store it on `ThriftOperationBackend` and pass it back for every subsequent RPC (GetStatus, FetchResults, Cancel, Close)
- When `TGetOperationStatusResp.operationState` is `RUNNING_STATE` or `PENDING_STATE`, poll using `RestStatementPoller`-equivalent logic (do not busy-spin)
- `TFetchResultsReq.orientation` must be `TFetchOrientation.FETCH_NEXT` (not `FETCH_FIRST`) on all calls after the initial fetch
- Check `TStatus.statusCode` on every response; map `ERROR_STATUS` and `STILL_EXECUTING_STATUS` to appropriate exception types; never silently ignore error status
- Thrift-generated classes may not have `[Serializable]` or nullability attributes; do not modify generated code — wrap in adapter classes instead
- Binary Thrift frames for test fixtures: serialise `TFetchResultsResp` to `byte[]` using `TMemoryBuffer` transport for use in fake service replayer

---

### D.4 — `arrow-dotnet.instructions.md`

**applyTo**: `**/src/BricksNet/Results/**/*.cs, **/tests/**/*Arrow*.cs`

Content must cover:
- Use `Apache.Arrow` NuGet; key namespaces: `Apache.Arrow`, `Apache.Arrow.Ipc`, `Apache.Arrow.Types`
- To read an Arrow IPC stream: `new ArrowStreamReader(stream)` then loop `await reader.ReadNextRecordBatchAsync()` until `null`; always dispose the reader
- `RecordBatch` is `IDisposable`; always use `using` or `await using` to release Arrow memory buffers
- Type check arrow arrays: `batch.Column(i)` returns `IArrowArray`; cast to the concrete type (e.g., `Int64Array`, `StringArray`) using `batch.Schema.GetFieldByIndex(i).DataType` for dispatch
- Null check: `array.IsNull(rowIndex)` before calling `array.GetValue(rowIndex)`; return `null` (not default value) for SQL NULLs
- `TimestampArray` values are `long` microseconds since Unix epoch; convert using `DateTimeOffset.FromUnixTimeMilliseconds(value / 1000).AddMicroseconds(value % 1000)`
- `Decimal128Array` values must be read as `decimal` using `array.GetValue(i)` which returns `decimal`; do not attempt manual 128-bit integer conversion
- For CloudFetch, Arrow IPC streams may be LZ4-framed; decompress with `K4os.Compression.LZ4.Streams.LZ4Stream.Decode(inputStream)` before passing to `ArrowStreamReader`
- When building test `RecordBatch` objects, use `RecordBatch.Builder` with `Append(IArrowArray)` calls; specify schema via `new Schema.Builder().Field(...)...Build()`
- Memory pressure: for large result sets, process one `RecordBatch` at a time and yield rows; do not accumulate all batches in memory before returning

---

### D.5 — `xunit-testing.instructions.md`

**applyTo**: `**/tests/**/*.cs`

Content must cover:
- Test class naming: `{ClassUnderTest}Tests` in the same relative namespace as the class under test but in the test project
- Use `[Theory]` + `[InlineData]` for data-driven tests; use `[MemberData]` for complex inputs that can't be inlined
- Async test methods: return `Task` (not `void`); never use `.GetAwaiter().GetResult()` inside a test — use `await` throughout
- Mocking: use `Moq` 4.20+; prefer `Mock<T>.Setup(x => x.Method()).ReturnsAsync(...)` over `callback`-style setups; verify critical calls with `Mock<T>.Verify(...)` in `Assert` phase
- HTTP mocking: use `RichardSzalay.MockHttp` (`MockHttpMessageHandler`) to mock `HttpClient` responses without starting a real server; pass the handler via `new HttpClient(mockHandler)`
- `IClassFixture<T>` for expensive shared setup (e.g., `FakeServiceFixture`); use constructor injection in test classes
- `IAsyncLifetime` on fixtures that need async setup/teardown (`InitializeAsync` / `DisposeAsync`)
- Test method structure: Arrange / Act / Assert with a blank line between each section; each test method tests exactly one behaviour
- Integration tests that require live Databricks credentials must be decorated `[Trait("Category", "E2E")]` and excluded from default test runs via `dotnet test --filter "Category!=E2E"`
- Fake service fixture pattern: the `FakeServiceFixture` starts a `TestServer` (via `WebApplicationFactory` or `TestHost`); the `Replayer` looks up the fixture file matching `{TestClass}/{TestMethod}/step-{n}.json`; each test method resets the step counter in `InitializeAsync`
- Code coverage: target 85% line coverage on `src/BricksNet/`; exclude `src/BricksNet/Backends/Thrift/Generated/` from coverage with `[ExcludeFromCodeCoverage]` attributes on generated files

---

### D.6 — `security-auth-dotnet.instructions.md`

**applyTo**: `**/src/BricksNet/Auth/**/*.cs, **/src/BricksNet/Connection/**/*.cs`

Content must cover:
- Never log credential values (`token`, `client_secret`, `code_verifier`, `access_token`, `refresh_token`); use `"***"` placeholders in all log output
- Never store credentials in `string` fields that survive past the request; prefer `ReadOnlySpan<char>` for transient handling, or clear memory explicitly with `CryptographicOperations.ZeroMemory` for sensitive byte arrays
- PKCE: `code_verifier` must be generated using `RandomNumberGenerator.GetBytes(96)`; `code_challenge` must be `Base64Url(SHA256(code_verifier))`; both must be single-use per authorization request
- CSRF protection: generate a random `state` parameter for every OAuth redirect using `RandomNumberGenerator.GetHexString(32)`; validate it on the callback before exchanging the code
- OAuth redirect listener: bind only to `127.0.0.1` (not `0.0.0.0`); use an ephemeral port; close the listener immediately after receiving one callback
- TLS: do not set `HttpClientHandler.ServerCertificateCustomValidationCallback` to always return `true`; if `AllowSelfSignedCerts` is true, log a `LogWarning` and validate only the certificate chain (not the hostname); include a code comment explaining the security risk
- Token cache: do not persist tokens to disk in plaintext; use `ProtectedData.Protect` (Windows DPAPI) or `IDataProtector` (ASP.NET Core Data Protection) when persisting to disk
- HTTP headers: use `Authorization: Bearer <token>` not `Authorization: Token <token>`; always use HTTPS (validate scheme at connection time; throw if `http://` is used)
- Input validation: validate `Host` does not contain path components; validate `HttpPath` starts with `/sql/`; prevent header injection by rejecting `\r`, `\n` in any value written to HTTP headers
- OWASP A02 (Cryptographic Failures): use `SHA256.HashData()` (static, no allocation) not `new SHA256Managed()`; use `HMACSHA256.HashData()` for HMAC operations

---

### D.7 — `nuget-packaging.instructions.md`

**applyTo**: `**/src/BricksNet/*.csproj, **/Directory.Build.props, **/Directory.Packages.props`

Content must cover:
- All shared MSBuild properties belong in `Directory.Build.props` at solution root; project-specific properties go in the individual `.csproj`
- Use `Directory.Packages.props` (Central Package Management) for all NuGet versions; set `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`; project `.csproj` files use `<PackageReference Include="..." />` without `Version` attribute
- Required packaging properties in `BricksNet.csproj`: `PackageId`, `Version`, `Authors`, `Company`, `Description`, `PackageLicenseExpression` (SPDX), `PackageReadmeFile`, `PackageProjectUrl`, `RepositoryUrl`, `RepositoryType=git`
- Source link: add `<PackageReference Include="Microsoft.SourceLink.GitHub">` to enable source-stepping in debuggers; this requires `EmbedUntrackedSources=true` and `Deterministic=true`
- Symbol package: set `IncludeSymbols=true` and `SymbolPackageFormat=snupkg`; the `.snupkg` file should be published alongside `.nupkg`
- Documentation XML: `GenerateDocumentationFile=true` in `BricksNet.csproj`; suppress `CS1591` (missing XML comment) only in test projects, not in the library
- Deterministic build: set `Deterministic=true` unconditionally and `ContinuousIntegrationBuild=true` when the `CI` environment variable is set; this ensures reproducible NuGet package hashes
- Do not embed appsettings or connection strings in the NuGet package; use `<None Include="..." Pack="false" />` to exclude test fixtures and development configs
- Test projects must not be packable: `<IsPackable>false</IsPackable>` in test `.csproj` files
- Versioning strategy: use `<Version>` in `Directory.Build.props` as a single source of truth; override with `VersionSuffix` for pre-release builds (e.g., `-alpha`, `-beta`); use GitVersion or MinVer NuGet tool for automated version bumping in CI

---

## Section E — Format & Quality Instructions for Opus

The following rules govern the quality and format of your output:

1. **Specificity**: Every implementation step must name the exact file path, class name, interface name, and NuGet package involved. "Create the HTTP transport" is not acceptable; "Create `BricksNetHttpClient` in `src/BricksNet/Connection/BricksNetHttpClient.cs` using `IHttpClientFactory`" is.

2. **Phase dependency graph**: At the top of `PLAN.md`, include a Mermaid `graph TD` diagram showing which phases must complete before others can start. Example:
   ```
   P1[Phase 1: Scaffolding] --> P2[Phase 2: Contracts]
   P2 --> P3[Phase 3: HTTP Transport]
   P3 --> P4[Phase 4: Auth]
   P3 --> P5[Phase 5: Thrift]
   ...
   ```

3. **Verification steps**: Each phase must end with a concrete, runnable verification: e.g., `dotnet build` passes; `dotnet test --filter ClassName=HttpRetryHandlerTests` passes; `dotnet pack` succeeds.

4. **Skills file format**: Each `.instructions.md` file must:
   - Have a valid YAML frontmatter block with `applyTo`
   - Use Markdown headings to group related rules
   - Include at least one code example for every non-trivial rule
   - Be scoped to the relevant glob (not `**/*.cs` globally)

5. **Cross-references**: When a phase relies on a class defined in a prior phase (e.g., Phase 5 uses `BricksNetHttpClient` from Phase 3), explicitly reference it: "Use `BricksNetHttpClient` (defined in Phase 3)".

6. **NuGet packages table**: Include a consolidated table of all NuGet packages and versions at the end of `PLAN.md` for use in `Directory.Packages.props`.

7. **Reference implementations**: Where the Node.js or Java implementations serve as a direct model, say so: e.g., "Mirror the `IOperationBackend` / `ISessionBackend` / `IBackend` three-layer pattern from `databricks-sql-nodejs/lib/contracts/`". This gives the implementation agent an additional reference when the plan alone is ambiguous.

8. **Output file paths**: State the exact output file paths at the top of each artefact:
   - Plan: `bricksnet/PLAN.md`
   - Skills files: `bricksnet/.github/instructions/<filename>.instructions.md`

9. **No placeholders**: Do not use `[TODO]`, `[TBD]`, or `[...]` in the plan. If something is intentionally deferred, say so explicitly: "OAuth2 PKCE browser redirect is implemented in Phase 4; the loopback port is hardcoded to 8020 with a fallback scan up to 8029."

10. **Language**: Write in the imperative voice for implementation steps ("Create", "Add", "Implement") and present tense for descriptions ("The `BricksNetHttpClient` owns the `HttpClient` lifetime").

---

*End of prompt.*
