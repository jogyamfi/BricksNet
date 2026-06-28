---
applyTo: "**/src/BricksNet/AdoNet/**/*.cs"
---

# ADO.NET Provider Patterns (BricksNet)

These rules apply to the `System.Data.Common` adapter layer in `src/BricksNet/AdoNet/`.

## Base Classes

- Inherit the abstract `System.Data.Common` base classes, not the `IDb*` interfaces. The base classes provide default implementations and keep the provider future-proof.

  ```csharp
  public sealed class BricksNetConnection : DbConnection { /* ... */ }
  public sealed class BricksNetCommand : DbCommand { /* ... */ }
  public sealed class BricksNetDataReader : DbDataReader { /* ... */ }
  ```

## Connection State

- Keep `DbConnection.State` accurate at all times through the `ConnectionState` transitions `Closed → Connecting → Open → Closed`. Never leave the state stale after an exception.

  ```csharp
  public override async Task OpenAsync(CancellationToken cancellationToken)
  {
      SetState(ConnectionState.Connecting);
      try
      {
          _client = new BricksNetClient(_options);
          await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
          _session = await _client.OpenSessionAsync(_sessionOptions, cancellationToken).ConfigureAwait(false);
          SetState(ConnectionState.Open);
      }
      catch
      {
          SetState(ConnectionState.Closed);
          throw;
      }
  }
  ```

## Sync over Async

- Sync `Open()`/`Close()` must call the async variant via `GetAwaiter().GetResult()` and document that callers should prefer the async methods to avoid deadlocks.

  ```csharp
  // Prefer OpenAsync(). This blocks the calling thread and can deadlock under a
  // single-threaded SynchronizationContext.
  public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
  ```

## Command & Reader

- Override `ExecuteDbDataReader` (not a public `ExecuteReader`); it must return `DbDataReader`.

  ```csharp
  protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
      => ExecuteDbDataReaderAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();
  ```

- `BricksNetDataReader.GetSchemaTable()` must return a `DataTable` with the standard 24 column-schema columns named by `System.Data.Common.SchemaTableColumn` (e.g., `ColumnName`, `ColumnOrdinal`, `ColumnSize`, `NumericPrecision`, `NumericScale`, `DataType`, `AllowDBNull`, `ProviderType`).
- `RecordsAffected` returns -1 (Databricks SQL does not report affected rows for SELECT).

## Exceptions

- `BricksNetException` must inherit `DbException` and set `SqlState` and `ErrorCode`.

  ```csharp
  public sealed class BricksNetException : DbException
  {
      public BricksNetException(string message, string? sqlState, int errorCode, string? queryId = null)
          : base(message, errorCode)
      {
          SqlState = sqlState;
          QueryId = queryId;
      }

      public override string? SqlState { get; }
      public string? QueryId { get; }
  }
  ```

## Parameters

- `DbParameter.Direction` must only accept `ParameterDirection.Input`. Throw `NotSupportedException` for `Output`, `InputOutput`, and `ReturnValue`.

  ```csharp
  public override ParameterDirection Direction
  {
      get => ParameterDirection.Input;
      set
      {
          if (value != ParameterDirection.Input)
              throw new NotSupportedException("Only ParameterDirection.Input is supported.");
      }
  }
  ```

## Connection String Builder

- `BricksNetConnectionStringBuilder` must back every typed property with the base `this[string keyword]` indexer so values round-trip through `ConnectionString`.

  ```csharp
  public string? Host
  {
      get => TryGetValue("Host", out var v) ? (string?)v : null;
      set => this["Host"] = value;
  }
  ```

- Support keyword aliases: `Host`, `Server`, and `Data Source` map to the same option. Override `ContainsKey` and `TryGetValue` to normalize aliases before lookup.

## Transactions

- `BeginDbTransaction(...)` throws `NotSupportedException` — Databricks SQL does not support multi-statement transactions.
