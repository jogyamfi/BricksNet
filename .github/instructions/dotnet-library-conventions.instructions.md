---
applyTo: "**/src/BricksNet/**/*.cs"
---

# .NET Library Conventions (BricksNet)

These rules apply to all production code in `src/BricksNet/`. They encode idiomatic, allocation-conscious, async-correct .NET 10 library design.

## Async & Cancellation

- Public async methods must return `Task`, `Task<T>`, or `ValueTask<T>`. Never use `async void` except for actual event handlers.
- Every `await` on a library code path must use `.ConfigureAwait(false)`.

  ```csharp
  // Correct
  var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
  ```

- Every public async method must accept `CancellationToken cancellationToken = default` as its **last** parameter and pass it through to every awaited call.

  ```csharp
  public async Task<IBricksNetOperation> ExecuteStatementAsync(
      string sql,
      StatementOptions? options = null,
      CancellationToken cancellationToken = default)
  {
      ArgumentException.ThrowIfNullOrEmpty(sql);
      return await _sessionBackend
          .ExecuteStatementAsync(sql, options, cancellationToken)
          .ConfigureAwait(false);
  }
  ```

- Never use `Thread.Sleep`. Use `await Task.Delay(delay, cancellationToken).ConfigureAwait(false)`.

## Disposal

- Classes that own `IDisposable`/`IAsyncDisposable` resources must implement `IAsyncDisposable`; implement `IDisposable` as well only when a synchronous disposal path is genuinely needed.
- A synchronous `Dispose()` may call `DisposeAsync().AsTask().GetAwaiter().GetResult()` only as a last resort; document the deadlock caveat in a comment.

  ```csharp
  // Prefer DisposeAsync(). Sync Dispose() blocks and may deadlock in single-threaded
  // sync contexts; provided only for `using` interop.
  public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
  ```

## Immutability & Types

- Use `record` types with `init`-only properties for all configuration and data-transfer objects.

  ```csharp
  public sealed record RetryOptions
  {
      public int MaxAttempts { get; init; } = 5;
      public int TimeoutMs { get; init; } = 60_000;
      public int DelayMinMs { get; init; } = 200;
      public int DelayMaxMs { get; init; } = 10_000;
  }
  ```

- Nullable reference types are enabled project-wide. Annotate every public API. Do not use the null-forgiving operator (`!`) to silence warnings without a justifying comment.
- Do not expose `List<T>` or `Dictionary<K,V>` in public APIs. Expose `IReadOnlyList<T>` and `IReadOnlyDictionary<K,V>`.

## Argument Validation

- Validate public API parameters with the BCL guard helpers:

  ```csharp
  ArgumentNullException.ThrowIfNull(options);
  ArgumentException.ThrowIfNullOrEmpty(sql);
  ArgumentOutOfRangeException.ThrowIfNegative(maxRows);
  ```

## Logging

- Use `[LoggerMessage]` source-generated log methods. Never call `_logger.LogXxx(...)` with interpolated strings (it boxes/allocates even when the level is disabled).

  ```csharp
  internal static partial class LogMessages
  {
      [LoggerMessage(Level = LogLevel.Debug, Message = "Retrying request (attempt {Attempt}) after {DelayMs}ms")]
      public static partial void Retrying(ILogger logger, int attempt, int delayMs);
  }
  ```

- Exception messages and logs must never contain sensitive data (tokens, credentials, or full SQL at `Error` level or above).

## General

- Prefer `sealed` classes unless a type is explicitly designed for inheritance.
- Use file-scoped namespaces and sort `System.*` usings first (enforced by `.editorconfig`).
