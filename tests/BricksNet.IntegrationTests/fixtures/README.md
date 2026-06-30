# Integration test fixtures

This directory documents the fixture layout used by the replay integration tests. The fake service
mirrors the Java driver's `fakeservice` record/replay pattern.

## Layout

```
fixtures/{service_type}/{test_name}/step-{n}.json
```

Each `step-{n}.json` file is a single recorded exchange:

```json
{
  "request":  { "method": "POST", "path": "/api/2.0/sql/statements" },
  "response": { "status": 200, "contentType": "application/json", "json": { ... } }
}
```

A response carries **either** a `json` body or a base64-encoded binary `base64` body (used for Arrow
IPC streams served as CloudFetch external chunks).

## Record vs. Replay

- **Replay** (default, offline): `FakeServiceReplayer` matches each request on HTTP method + path and
  returns the stored response. Selected by `dotnet test --filter "Category!=E2E"`.
- **Record**: `FakeServiceRecorder` forwards each request to a live workspace and writes the captured
  exchanges here. Requires credentials; never runs in CI.

## How these fixtures are produced

For determinism and to avoid checking in opaque binary, the REST/CloudFetch fixtures are materialised
at test start by `FakeService/FixtureLibrary.cs`, which builds wire-accurate Arrow IPC payloads with
`Apache.Arrow` and writes the `step-*.json` files into a temporary directory (the CloudFetch external
links are rewritten to the fake server's loopback URL). Re-capturing against a live workspace would
instead populate this directory via the recorder.

The Thrift suite (`ThriftBackendIntegrationTests`) runs only against a live workspace and is gated by
`[Trait("Category", "E2E")]`.
