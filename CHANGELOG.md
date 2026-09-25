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
