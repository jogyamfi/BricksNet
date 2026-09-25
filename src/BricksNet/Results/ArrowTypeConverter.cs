using Apache.Arrow;
using Apache.Arrow.Types;

namespace BricksNet.Results;

/// <summary>
/// Converts Apache Arrow <see cref="RecordBatch"/> columns into loosely-typed row dictionaries,
/// mapping each Arrow array type onto its idiomatic .NET representation. SQL <c>NULL</c>s
/// (<c>array.IsNull(i)</c>) are surfaced as <see langword="null"/>.
/// </summary>
internal static class ArrowTypeConverter
{
    /// <summary>
    /// Materialises every row of <paramref name="batch"/> as an ordered dictionary keyed by
    /// column name.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ConvertBatch(RecordBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var columnCount = batch.ColumnCount;
        var rowCount = batch.Length;
        var fields = batch.Schema.FieldsList;
        var names = new string[columnCount];
        for (var col = 0; col < columnCount; col++)
        {
            names[col] = fields[col].Name;
        }

        names = ColumnNames.MakeUnique(names);

        var rows = new List<IReadOnlyDictionary<string, object?>>(rowCount);
        for (var row = 0; row < rowCount; row++)
        {
            var record = new Dictionary<string, object?>(columnCount, StringComparer.Ordinal);
            for (var col = 0; col < columnCount; col++)
            {
                record[names[col]] = ConvertValue(batch.Column(col), row);
            }

            rows.Add(record);
        }

        return rows;
    }

    /// <summary>Converts a single cell of an Arrow array to its .NET value (or <see langword="null"/>).</summary>
    public static object? ConvertValue(IArrowArray array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);

        if (array.IsNull(index))
        {
            return null;
        }

        return array switch
        {
            Int8Array a => a.GetValue(index),
            Int16Array a => a.GetValue(index),
            Int32Array a => a.GetValue(index),
            Int64Array a => a.GetValue(index),
            UInt8Array a => a.GetValue(index),
            UInt16Array a => a.GetValue(index),
            UInt32Array a => a.GetValue(index),
            UInt64Array a => a.GetValue(index),
            FloatArray a => a.GetValue(index),
            DoubleArray a => a.GetValue(index),
            BooleanArray a => a.GetValue(index),
            Decimal128Array a => a.GetValue(index),
            Decimal256Array a => a.GetValue(index),
            Date32Array a => ToDateOnly(a.GetDateTimeOffset(index)),
            Date64Array a => ToDateOnly(a.GetDateTimeOffset(index)),
            TimestampArray a => a.GetTimestamp(index),
            StringArray a => a.GetString(index),
            LargeStringArray a => a.GetString(index),
            BinaryArray a => a.GetBytes(index).ToArray(),
            LargeBinaryArray a => a.GetBytes(index).ToArray(),
            ListArray a => ConvertList(a, index),
            StructArray a => ConvertStruct(a, index),
            _ => throw new NotSupportedException(
                $"Arrow array type '{array.Data.DataType.TypeId}' is not supported."),
        };
    }

    private static DateOnly? ToDateOnly(DateTimeOffset? value) =>
        value is { } dto ? DateOnly.FromDateTime(dto.UtcDateTime) : null;

    private static object?[] ConvertList(ListArray array, int index)
    {
        var values = array.GetSlicedValues(index);
        var length = values.Length;
        var result = new object?[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = ConvertValue(values, i);
        }

        return result;
    }

    private static Dictionary<string, object?> ConvertStruct(StructArray array, int index)
    {
        var structType = (StructType)array.Data.DataType;
        var fields = array.Fields;
        var result = new Dictionary<string, object?>(fields.Count, StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            result[structType.Fields[i].Name] = ConvertValue(fields[i], index);
        }

        return result;
    }
}
