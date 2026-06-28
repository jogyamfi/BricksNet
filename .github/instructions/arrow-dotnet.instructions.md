---
applyTo: "**/src/BricksNet/Results/**/*.cs, **/tests/**/*Arrow*.cs"
---

# Apache Arrow on .NET (BricksNet)

These rules apply to Arrow result handling in `src/BricksNet/Results/` and any Arrow tests.

## Package & Namespaces

- Use the `Apache.Arrow` NuGet package. Key namespaces: `Apache.Arrow`, `Apache.Arrow.Ipc`, `Apache.Arrow.Types`.

## Reading IPC Streams

- Read an Arrow IPC stream with `ArrowStreamReader`; loop until `ReadNextRecordBatchAsync` returns `null`; always dispose the reader.

  ```csharp
  using var reader = new ArrowStreamReader(stream);
  RecordBatch? batch;
  while ((batch = await reader.ReadNextRecordBatchAsync(cancellationToken).ConfigureAwait(false)) is not null)
  {
      using (batch)
      {
          // process exactly one batch, then release it
      }
  }
  ```

- `RecordBatch` is `IDisposable`. Always wrap it in `using`/`await using` to release Arrow memory buffers promptly.

## Type Dispatch

- `batch.Column(i)` returns `IArrowArray`. Dispatch on `batch.Schema.GetFieldByIndex(i).DataType` (or `DataType.TypeId`) and cast to the concrete array type.

  ```csharp
  IArrowArray column = batch.Column(i);
  object? value = column switch
  {
      Int64Array a => a.IsNull(row) ? null : a.GetValue(row),
      StringArray s => s.GetString(row), // returns null for SQL NULL
      _ => throw new NotSupportedException($"Unsupported Arrow type {column.Data.DataType.TypeId}")
  };
  ```

## Null Handling

- Call `array.IsNull(rowIndex)` before reading. Return `null` (not a default value) for SQL NULLs.

## Specific Type Conversions

- `TimestampArray` values are `long` microseconds since the Unix epoch:

  ```csharp
  long micros = tsArray.GetValue(row)!.Value;
  var dto = DateTimeOffset.FromUnixTimeMilliseconds(micros / 1000).AddMicroseconds(micros % 1000);
  ```

- `Decimal128Array.GetValue(i)` already returns a `decimal`. Read it directly; do not perform manual 128-bit integer reconstruction.

## CloudFetch + LZ4

- CloudFetch Arrow IPC streams may be LZ4-framed. Decompress before handing to `ArrowStreamReader`:

  ```csharp
  using var decompressed = LZ4Stream.Decode(inputStream);
  using var reader = new ArrowStreamReader(decompressed);
  ```

## Building Test Batches

- Build test `RecordBatch` objects with `RecordBatch.Builder` and an explicit schema:

  ```csharp
  var schema = new Schema.Builder()
      .Field(f => f.Name("id").DataType(Int64Type.Default))
      .Field(f => f.Name("name").DataType(StringType.Default))
      .Build();

  var batch = new RecordBatch.Builder()
      .Append("id", false, col => col.Int64(a => a.AppendRange(new long[] { 1, 2 })))
      .Append("name", false, col => col.String(a => a.AppendRange(new[] { "a", "b" })))
      .Build();
  ```

## Memory Pressure

- For large result sets, process one `RecordBatch` at a time and yield rows. Do not accumulate every batch in memory before returning.
