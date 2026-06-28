---
applyTo: "**/src/BricksNet/Backends/Thrift/**/*.cs"
---

# Thrift on .NET (BricksNet)

These rules apply to the Thrift backend in `src/BricksNet/Backends/Thrift/`.

## Package & Namespaces

- Use the `Apache.Thrift` NuGet package. Import `Thrift.Protocol` and `Thrift.Transport`.
- Generated types live under `src/BricksNet/Backends/Thrift/Generated/` and are produced with:

  ```text
  thrift --gen netstd:wcf=false -out src/BricksNet/Backends/Thrift/Generated src/BricksNet/Backends/Thrift/idl/TCLIService.thrift
  ```

  Do not hand-edit generated code. Mark generated files `[ExcludeFromCodeCoverage]`.

## Async RPCs

- Always use the async RPC overloads (`OpenSessionAsync`, `ExecuteStatementAsync`, `GetOperationStatusAsync`, `FetchResultsAsync`, etc.) and propagate `CancellationToken`.

  ```csharp
  var resp = await _client.ExecuteStatementAsync(req, cancellationToken).ConfigureAwait(false);
  ```

## Transport Framing

- Build a `TBinaryProtocol` over a `TStreamTransport` wrapping the raw HTTP request/response body. Read the full HTTP response body into a `MemoryStream` **before** creating the protocol — `HttpContent.ReadAsStreamAsync` returns a forward-only stream and Thrift needs a seekable buffer.

  ```csharp
  var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
  using var buffer = new MemoryStream(bytes);
  var transport = new TStreamTransport(buffer, null, _config);
  var protocol = new TBinaryProtocol(transport);
  ```

## Operation Handles

- `TOperationHandle` is opaque. Store it on `ThriftOperationBackend` and pass it back for every subsequent RPC (`GetStatus`, `FetchResults`, `Cancel`, `Close`). Do not reconstruct it.

## Polling

- When `TGetOperationStatusResp.operationState` is `RUNNING_STATE` or `PENDING_STATE`, poll with backoff (start 200 ms, cap 10 s). Do not busy-spin.

  ```csharp
  while (state is TOperationState.RUNNING_STATE or TOperationState.PENDING_STATE)
  {
      await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
      delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 10_000));
      // re-poll status...
  }
  ```

## Fetch Orientation

- Use `TFetchOrientation.FETCH_NEXT` on every fetch after the initial one. Never `FETCH_FIRST` mid-stream (it rewinds the cursor).

## Status Checking

- Check `TStatus.statusCode` on every response. Map `ERROR_STATUS` and `STILL_EXECUTING_STATUS` to exceptions via `ThriftStatusChecker.EnsureSuccess`. Never silently ignore an error status.

  ```csharp
  internal static void EnsureSuccess(TStatus status)
  {
      if (status.StatusCode is TStatusCode.ERROR_STATUS or TStatusCode.STILL_EXECUTING_STATUS)
          throw new BricksNetException(status.ErrorMessage ?? "Thrift error",
              status.SqlState, status.ErrorCode);
  }
  ```

## Wrapping Generated Code

- Generated classes may lack `[Serializable]` or nullability attributes. Do not modify them. Wrap them in adapter classes (`ThriftSessionBackend`, `ThriftOperationBackend`) that expose the `ISessionBackend`/`IOperationBackend` contracts.

## Test Fixtures

- To produce binary Thrift frames for the fake-service replayer, serialise responses (e.g., `TFetchResultsResp`) to `byte[]` using a `TMemoryBuffer` transport, then base64-encode for storage in JSON fixtures.

  ```csharp
  var membuf = new TMemoryBufferTransport(_config);
  var proto = new TBinaryProtocol(membuf);
  await resp.WriteAsync(proto, CancellationToken.None).ConfigureAwait(false);
  var base64 = Convert.ToBase64String(membuf.GetBuffer());
  ```
