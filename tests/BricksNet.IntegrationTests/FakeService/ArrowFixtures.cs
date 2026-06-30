using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// Builds Arrow IPC streams used as inline / CloudFetch result payloads in fixtures. Using
/// <see cref="ArrowStreamWriter"/> guarantees the bytes are wire-identical to what the SQL
/// Statement Execution API emits for the <c>ARROW_STREAM</c> format.
/// </summary>
internal static class ArrowFixtures
{
    /// <summary>A row in the canonical sample table (<c>id INT, name STRING, active BOOLEAN</c>).</summary>
    public readonly record struct SampleRow(int Id, string? Name, bool Active);

    /// <summary>Builds a single-batch Arrow stream for the canonical sample table.</summary>
    public static async Task<byte[]> BuildSampleTableAsync(IReadOnlyList<SampleRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var schema = new Schema(
            new List<Field>
            {
                new("id", Int32Type.Default, nullable: false),
                new("name", StringType.Default, nullable: true),
                new("active", BooleanType.Default, nullable: false),
            },
            metadata: null);

        var idBuilder = new Int32Array.Builder();
        var nameBuilder = new StringArray.Builder();
        var activeBuilder = new BooleanArray.Builder();

        foreach (var row in rows)
        {
            idBuilder.Append(row.Id);
            if (row.Name is null)
            {
                nameBuilder.AppendNull();
            }
            else
            {
                nameBuilder.Append(row.Name);
            }

            activeBuilder.Append(row.Active);
        }

        var arrays = new IArrowArray[]
        {
            idBuilder.Build(),
            nameBuilder.Build(),
            activeBuilder.Build(),
        };

        using var batch = new RecordBatch(schema, arrays, rows.Count);
        return await WriteAsync(schema, batch).ConfigureAwait(false);
    }

    /// <summary>Builds a single-batch Arrow stream with a single <c>id INT</c> column.</summary>
    public static async Task<byte[]> BuildIdColumnAsync(IReadOnlyList<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var schema = new Schema(
            new List<Field> { new("id", Int32Type.Default, nullable: false) },
            metadata: null);

        var builder = new Int32Array.Builder();
        foreach (var id in ids)
        {
            builder.Append(id);
        }

        using var batch = new RecordBatch(schema, new IArrowArray[] { builder.Build() }, ids.Count);
        return await WriteAsync(schema, batch).ConfigureAwait(false);
    }

    private static async Task<byte[]> WriteAsync(Schema schema, RecordBatch batch)
    {
        using var stream = new MemoryStream();
        using (var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true))
        {
            await writer.WriteRecordBatchAsync(batch).ConfigureAwait(false);
            await writer.WriteEndAsync().ConfigureAwait(false);
        }

        return stream.ToArray();
    }
}
