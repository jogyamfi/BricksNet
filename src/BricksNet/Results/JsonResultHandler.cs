using System.Globalization;
using System.Text.Json;
using BricksNet.Contracts;

namespace BricksNet.Results;

/// <summary>
/// Parses an inline JSON result payload (the <c>JSON_ARRAY</c> disposition of the SQL Statement
/// Execution API): a JSON array of row arrays whose cells are JSON strings. Values are coerced to
/// .NET types using the supplied <see cref="ColumnInfo"/> schema. The entire payload is returned by
/// the first <see cref="FetchNextChunkAsync"/> call.
/// </summary>
internal sealed class JsonResultHandler : IResultsProvider
{
    private readonly ReadOnlyMemory<byte> _payload;
    private readonly IReadOnlyList<ColumnInfo> _schema;
    private readonly string[] _names;
    private bool _hasMoreRows;

    /// <summary>Creates a handler over a buffered JSON payload and its column schema.</summary>
    public JsonResultHandler(ReadOnlyMemory<byte> payload, IReadOnlyList<ColumnInfo> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _payload = payload;
        _schema = schema;
        _names = ColumnNames.MakeUnique(schema.Select(column => column.Name).ToArray());
        _hasMoreRows = !payload.IsEmpty;
    }

    /// <inheritdoc />
    public bool HasMoreRows => _hasMoreRows;

    /// <inheritdoc />
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FetchNextChunkAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_hasMoreRows)
        {
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
                Array.Empty<IReadOnlyDictionary<string, object?>>());
        }

        _hasMoreRows = false;
        return Task.FromResult(Parse());
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> Parse()
    {
        using var document = JsonDocument.Parse(_payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(root.GetArrayLength());
        foreach (var rowElement in root.EnumerateArray())
        {
            var record = new Dictionary<string, object?>(_schema.Count, StringComparer.Ordinal);
            var ordinal = 0;
            foreach (var cell in rowElement.EnumerateArray())
            {
                if (ordinal >= _schema.Count)
                {
                    break;
                }

                record[_names[ordinal]] = Coerce(cell, _schema[ordinal].TypeName);
                ordinal++;
            }

            rows.Add(record);
        }

        return rows;
    }

    private static object? Coerce(JsonElement cell, string typeName)
    {
        if (cell.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var text = cell.ValueKind == JsonValueKind.String ? cell.GetString() : cell.GetRawText();
        if (text is null)
        {
            return null;
        }

        var normalized = NormalizeTypeName(typeName);
        return normalized switch
        {
            "BOOLEAN" => bool.Parse(text),
            "TINYINT" or "BYTE" => sbyte.Parse(text, CultureInfo.InvariantCulture),
            "SMALLINT" or "SHORT" => short.Parse(text, CultureInfo.InvariantCulture),
            "INT" or "INTEGER" => int.Parse(text, CultureInfo.InvariantCulture),
            "BIGINT" or "LONG" => long.Parse(text, CultureInfo.InvariantCulture),
            "FLOAT" or "REAL" => float.Parse(text, CultureInfo.InvariantCulture),
            "DOUBLE" => double.Parse(text, CultureInfo.InvariantCulture),
            "DECIMAL" or "NUMERIC" => ParseDecimalOrString(text),
            "DATE" => DateOnly.Parse(text, CultureInfo.InvariantCulture),
            "TIMESTAMP" => DateTimeOffset.Parse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            // A wall-clock value: keep the written date and time, with no time zone attached.
            "TIMESTAMP_NTZ" => DateTimeOffset.Parse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).DateTime,
            "BINARY" => Convert.FromBase64String(text),
            _ => text,
        };
    }

    // DECIMAL(38, s) values can exceed System.Decimal; keep the exact text rather than failing.
    private static object ParseDecimalOrString(string text) =>
        decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value)
            ? value
            : text;

    private static string NormalizeTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return string.Empty;
        }

        var parenIndex = typeName.IndexOf('(', StringComparison.Ordinal);
        var bare = parenIndex >= 0 ? typeName[..parenIndex] : typeName;
        return bare.Trim().ToUpperInvariant();
    }
}
