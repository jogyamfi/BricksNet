using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using BricksNet.Contracts;
using BricksNet.Results;

namespace BricksNet.AdoNet;

/// <summary>
/// Forward-only, read-only cursor over the rows of a completed Databricks operation.
/// Rows are pulled lazily in chunks from the underlying <see cref="IBricksNetOperation"/>;
/// column metadata is derived from the first materialised chunk.
/// </summary>
public sealed class BricksNetDataReader : DbDataReader, IAsyncEnumerable<IDataRecord>
{
    private const string DataTypeNameColumn = "DataTypeName";

    private readonly IBricksNetOperation _operation;
    private readonly int _chunkSize;
    private readonly CommandBehavior _behavior;
    private readonly DbConnection? _connectionToClose;

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> _currentChunk;
    private IReadOnlyList<Column> _columns;
    private int _rowInChunk = -1;
    private bool _exhausted;
    private bool _closed;

    internal BricksNetDataReader(
        IBricksNetOperation operation,
        IReadOnlyList<ColumnInfo> schema,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> firstChunk,
        int chunkSize,
        CommandBehavior behavior,
        DbConnection? connectionToClose)
    {
        _operation = operation;
        _currentChunk = firstChunk;
        _chunkSize = chunkSize <= 0 ? 10_000 : chunkSize;
        _behavior = behavior;
        _connectionToClose = connectionToClose;
        _columns = schema.Count > 0 ? BuildColumns(schema, firstChunk) : InferColumns(firstChunk);
        _exhausted = firstChunk.Count == 0;
    }

    /// <inheritdoc />
    public override int FieldCount => _columns.Count;

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override bool HasRows => _currentChunk.Count > 0;

    /// <inheritdoc />
    public override bool IsClosed => _closed;

    /// <inheritdoc />
    public override int RecordsAffected => -1;

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override bool Read() => ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        if (_rowInChunk + 1 < _currentChunk.Count)
        {
            _rowInChunk++;
            return true;
        }

        if (_exhausted)
        {
            return false;
        }

        var next = await _operation
            .FetchChunkAsync<IReadOnlyDictionary<string, object?>>(_chunkSize, cancellationToken)
            .ConfigureAwait(false);

        if (next.Count == 0)
        {
            _exhausted = true;
            return false;
        }

        if (_columns.Count == 0)
        {
            _columns = InferColumns(next);
        }

        _currentChunk = next;
        _rowInChunk = 0;
        return true;
    }

    /// <inheritdoc />
    public override bool NextResult() => false;

    /// <inheritdoc />
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    /// <inheritdoc />
    public override string GetName(int ordinal) => _columns[ordinal].Name;

    /// <inheritdoc />
    public override int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (var i = 0; i < _columns.Count; i++)
        {
            if (string.Equals(_columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new IndexOutOfRangeException($"No column named '{name}' was found in the result set.");
    }

    /// <inheritdoc />
    public override Type GetFieldType(int ordinal) => _columns[ordinal].ClrType;

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => _columns[ordinal].DataTypeName;

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        var value = GetRawValue(ordinal);
        return value ?? DBNull.Value;
    }

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var count = Math.Min(values.Length, _columns.Count);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal) => GetRawValue(ordinal) is null;

    /// <inheritdoc />
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) =>
        Task.FromResult(IsDBNull(ordinal));

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => Convert.ToByte(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override char GetChar(int ordinal) => Convert.ToChar(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => Convert.ToSingle(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => Convert.ToDouble(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetNonNull(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string GetString(int ordinal) =>
        GetNonNull(ordinal) as string ?? Convert.ToString(GetNonNull(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal) => GetNonNull(ordinal) switch
    {
        DateTime dt => dt,
        DateTimeOffset dto => dto.UtcDateTime,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        var other => Convert.ToDateTime(other, CultureInfo.InvariantCulture),
    };

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal) => GetNonNull(ordinal) switch
    {
        Guid guid => guid,
        string s => Guid.Parse(s),
        var other => throw new InvalidCastException(
            $"Cannot convert value of type '{other.GetType()}' to Guid."),
    };

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var bytes = (byte[])GetNonNull(ordinal);
        if (buffer is null)
        {
            return bytes.Length;
        }

        var available = Math.Max(0, bytes.Length - (int)dataOffset);
        var toCopy = Math.Min(available, length);
        Array.Copy(bytes, dataOffset, buffer, bufferOffset, toCopy);
        return toCopy;
    }

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var chars = GetString(ordinal).ToCharArray();
        if (buffer is null)
        {
            return chars.Length;
        }

        var available = Math.Max(0, chars.Length - (int)dataOffset);
        var toCopy = Math.Min(available, length);
        Array.Copy(chars, dataOffset, buffer, bufferOffset, toCopy);
        return toCopy;
    }

    /// <inheritdoc />
    public override T GetFieldValue<T>(int ordinal)
    {
        var value = GetRawValue(ordinal);
        if (value is null)
        {
            if (default(T) is null)
            {
                return (T)(object?)null!;
            }

            throw new InvalidCastException($"Column '{GetName(ordinal)}' is NULL and cannot be cast to '{typeof(T)}'.");
        }

        if (value is T typed)
        {
            return typed;
        }

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <inheritdoc />
    public override DataTable GetSchemaTable()
    {
        var table = new DataTable("SchemaTable") { Locale = CultureInfo.InvariantCulture };
        table.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
        table.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
        table.Columns.Add(SchemaTableColumn.ColumnSize, typeof(int));
        table.Columns.Add(SchemaTableColumn.NumericPrecision, typeof(short));
        table.Columns.Add(SchemaTableColumn.NumericScale, typeof(short));
        table.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
        table.Columns.Add(SchemaTableColumn.ProviderType, typeof(int));
        table.Columns.Add(SchemaTableColumn.IsLong, typeof(bool));
        table.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));
        table.Columns.Add(SchemaTableColumn.IsAliased, typeof(bool));
        table.Columns.Add(SchemaTableColumn.IsExpression, typeof(bool));
        table.Columns.Add(SchemaTableColumn.IsKey, typeof(bool));
        table.Columns.Add(SchemaTableColumn.IsUnique, typeof(bool));
        table.Columns.Add(SchemaTableColumn.BaseColumnName, typeof(string));
        table.Columns.Add(SchemaTableColumn.BaseSchemaName, typeof(string));
        table.Columns.Add(SchemaTableColumn.BaseTableName, typeof(string));
        table.Columns.Add(SchemaTableOptionalColumn.BaseCatalogName, typeof(string));
        table.Columns.Add(SchemaTableOptionalColumn.BaseServerName, typeof(string));
        table.Columns.Add(SchemaTableOptionalColumn.IsAutoIncrement, typeof(bool));
        table.Columns.Add(SchemaTableOptionalColumn.IsHidden, typeof(bool));
        table.Columns.Add(SchemaTableOptionalColumn.IsReadOnly, typeof(bool));
        table.Columns.Add(SchemaTableOptionalColumn.IsRowVersion, typeof(bool));
        table.Columns.Add(DataTypeNameColumn, typeof(string));
        table.Columns.Add(SchemaTableColumn.NonVersionedProviderType, typeof(int));

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var row = table.NewRow();
            row[SchemaTableColumn.ColumnName] = column.Name;
            row[SchemaTableColumn.ColumnOrdinal] = i;
            row[SchemaTableColumn.ColumnSize] = -1;
            row[SchemaTableColumn.NumericPrecision] = column.Precision is { } precision ? (short)precision : DBNull.Value;
            row[SchemaTableColumn.NumericScale] = column.Scale is { } scale ? (short)scale : DBNull.Value;
            row[SchemaTableColumn.DataType] = column.ClrType;
            row[SchemaTableColumn.ProviderType] = 0;
            row[SchemaTableColumn.IsLong] = column.ClrType == typeof(byte[]) || column.ClrType == typeof(string);
            row[SchemaTableColumn.AllowDBNull] = true;
            row[SchemaTableColumn.IsAliased] = false;
            row[SchemaTableColumn.IsExpression] = false;
            row[SchemaTableColumn.IsKey] = false;
            row[SchemaTableColumn.IsUnique] = false;
            row[SchemaTableColumn.BaseColumnName] = column.Name;
            row[SchemaTableColumn.BaseSchemaName] = DBNull.Value;
            row[SchemaTableColumn.BaseTableName] = DBNull.Value;
            row[SchemaTableOptionalColumn.BaseCatalogName] = DBNull.Value;
            row[SchemaTableOptionalColumn.BaseServerName] = DBNull.Value;
            row[SchemaTableOptionalColumn.IsAutoIncrement] = false;
            row[SchemaTableOptionalColumn.IsHidden] = false;
            row[SchemaTableOptionalColumn.IsReadOnly] = true;
            row[SchemaTableOptionalColumn.IsRowVersion] = false;
            row[DataTypeNameColumn] = column.DataTypeName;
            row[SchemaTableColumn.NonVersionedProviderType] = 0;
            table.Rows.Add(row);
        }

        return table;
    }

    /// <inheritdoc />
    public IAsyncEnumerator<IDataRecord> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new AsyncRecordEnumerator(this, cancellationToken);

    /// <inheritdoc />
    public override async Task CloseAsync()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        await _operation.CloseAsync().ConfigureAwait(false);

        if (_behavior.HasFlag(CommandBehavior.CloseConnection) && _connectionToClose is not null)
        {
            await _connectionToClose.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override void Close() => CloseAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private object? GetRawValue(int ordinal)
    {
        ThrowIfClosed();
        if (_rowInChunk < 0 || _rowInChunk >= _currentChunk.Count)
        {
            throw new InvalidOperationException("No row is available. Call Read() before accessing column values.");
        }

        if ((uint)ordinal >= (uint)_columns.Count)
        {
            throw new IndexOutOfRangeException($"Column ordinal {ordinal} is out of range.");
        }

        return _currentChunk[_rowInChunk].TryGetValue(_columns[ordinal].Name, out var value) ? value : null;
    }

    private object GetNonNull(int ordinal) =>
        GetRawValue(ordinal) ?? throw new InvalidOperationException(
            $"Column '{GetName(ordinal)}' is NULL. Check IsDBNull before reading.");

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new InvalidOperationException("The data reader is closed.");
        }
    }

    /// <summary>
    /// Builds columns from the server-reported schema so names, SQL type names, precision and scale
    /// are available even for empty results or columns whose sampled values are all NULL. The CLR
    /// type prefers the type actually observed in <paramref name="firstChunk"/>, because some
    /// backends deliver values (e.g. complex types) in a representation other than the declared one.
    /// </summary>
    private static IReadOnlyList<Column> BuildColumns(
        IReadOnlyList<ColumnInfo> schema,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> firstChunk)
    {
        var names = ColumnNames.MakeUnique(schema.Select(column => column.Name).ToArray());
        var columns = new Column[schema.Count];
        for (var i = 0; i < schema.Count; i++)
        {
            var info = schema[i];
            var clrType = ObservedType(firstChunk, names[i]) ?? MapClrType(info.TypeName);
            columns[i] = new Column(names[i], clrType, info.TypeName, info.Precision, info.Scale);
        }

        return columns;
    }

    private static Type? ObservedType(IReadOnlyList<IReadOnlyDictionary<string, object?>> chunk, string name)
    {
        foreach (var row in chunk)
        {
            if (row.TryGetValue(name, out var value) && value is not null)
            {
                return value.GetType();
            }
        }

        return null;
    }

    private static Type MapClrType(string typeName)
    {
        var parenIndex = typeName.IndexOf('(', StringComparison.Ordinal);
        var bare = (parenIndex >= 0 ? typeName[..parenIndex] : typeName).Trim().ToUpperInvariant();
        return bare switch
        {
            "BOOLEAN" => typeof(bool),
            "TINYINT" or "BYTE" => typeof(sbyte),
            "SMALLINT" or "SHORT" => typeof(short),
            "INT" or "INTEGER" => typeof(int),
            "BIGINT" or "LONG" => typeof(long),
            "FLOAT" or "REAL" => typeof(float),
            "DOUBLE" => typeof(double),
            "DECIMAL" or "NUMERIC" => typeof(decimal),
            "STRING" or "VARCHAR" or "CHAR" => typeof(string),
            "DATE" => typeof(DateOnly),
            "TIMESTAMP" or "TIMESTAMP_NTZ" => typeof(DateTimeOffset),
            "BINARY" => typeof(byte[]),
            _ => typeof(object),
        };
    }

    private static IReadOnlyList<Column> InferColumns(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> chunk)
    {
        if (chunk.Count == 0)
        {
            return [];
        }

        var names = chunk[0].Keys.ToArray();
        var columns = new Column[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var clrType = typeof(object);
            foreach (var row in chunk)
            {
                if (row.TryGetValue(names[i], out var value) && value is not null)
                {
                    clrType = value.GetType();
                    break;
                }
            }

            columns[i] = new Column(names[i], clrType, MapDataTypeName(clrType), Precision: null, Scale: null);
        }

        return columns;
    }

    private static string MapDataTypeName(Type clrType) => Type.GetTypeCode(clrType) switch
    {
        TypeCode.Boolean => "BOOLEAN",
        TypeCode.SByte => "TINYINT",
        TypeCode.Byte => "TINYINT",
        TypeCode.Int16 => "SMALLINT",
        TypeCode.Int32 => "INT",
        TypeCode.Int64 => "BIGINT",
        TypeCode.Single => "FLOAT",
        TypeCode.Double => "DOUBLE",
        TypeCode.Decimal => "DECIMAL",
        TypeCode.String => "STRING",
        TypeCode.DateTime => "TIMESTAMP",
        _ when clrType == typeof(DateOnly) => "DATE",
        _ when clrType == typeof(DateTimeOffset) => "TIMESTAMP",
        _ when clrType == typeof(byte[]) => "BINARY",
        _ => "STRING",
    };

    private readonly record struct Column(
        string Name,
        Type ClrType,
        string DataTypeName,
        int? Precision,
        int? Scale);

    private sealed class AsyncRecordEnumerator(BricksNetDataReader reader, CancellationToken cancellationToken)
        : IAsyncEnumerator<IDataRecord>
    {
        public IDataRecord Current => reader;

        public async ValueTask<bool> MoveNextAsync() =>
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
