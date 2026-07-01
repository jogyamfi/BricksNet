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

[Unreleased]: https://github.com/your-org/bricksnet/compare/HEAD...HEAD
