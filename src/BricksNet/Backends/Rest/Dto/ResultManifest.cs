namespace BricksNet.Backends.Rest.Dto;

/// <summary>
/// Describes the shape of a statement's result set: serialisation format, column schema,
/// and the index of result chunks.
/// </summary>
internal sealed class ResultManifest
{
    /// <summary>Result serialisation format (<c>JSON_ARRAY</c> or <c>ARROW_STREAM</c>).</summary>
    public string? Format { get; set; }

    /// <summary>Column metadata for the result set.</summary>
    public ResultSchema? Schema { get; set; }

    /// <summary>Total number of result chunks.</summary>
    public long TotalChunkCount { get; set; }

    /// <summary>Total number of rows across all chunks.</summary>
    public long TotalRowCount { get; set; }

    /// <summary>Total uncompressed size of the result in bytes.</summary>
    public long TotalByteCount { get; set; }

    /// <summary>Per-chunk index entries (without the data itself).</summary>
    public IReadOnlyList<ResultChunkIndex>? Chunks { get; set; }

    /// <summary>Indicates the result was truncated because it exceeded the row or byte limit.</summary>
    public bool Truncated { get; set; }
}

/// <summary>Column schema of a result set.</summary>
internal sealed class ResultSchema
{
    /// <summary>Number of columns.</summary>
    public int ColumnCount { get; set; }

    /// <summary>The column definitions in ordinal order.</summary>
    public IReadOnlyList<ColumnSchema>? Columns { get; set; }
}

/// <summary>Metadata for a single result column.</summary>
internal sealed class ColumnSchema
{
    /// <summary>Column name.</summary>
    public string? Name { get; set; }

    /// <summary>Canonical SQL type name (for example <c>INT</c> or <c>STRING</c>).</summary>
    public string? TypeName { get; set; }

    /// <summary>Full SQL type text including parameters (for example <c>DECIMAL(10,2)</c>).</summary>
    public string? TypeText { get; set; }

    /// <summary>Zero-based ordinal position of the column.</summary>
    public int Position { get; set; }

    /// <summary>Decimal precision, when applicable.</summary>
    public int? TypePrecision { get; set; }

    /// <summary>Decimal scale, when applicable.</summary>
    public int? TypeScale { get; set; }
}

/// <summary>Index entry describing a single result chunk without its payload.</summary>
internal sealed class ResultChunkIndex
{
    /// <summary>Zero-based index of this chunk.</summary>
    public long ChunkIndex { get; set; }

    /// <summary>Row offset of the first row in this chunk.</summary>
    public long RowOffset { get; set; }

    /// <summary>Number of rows in this chunk.</summary>
    public long RowCount { get; set; }

    /// <summary>Uncompressed size of this chunk in bytes.</summary>
    public long ByteCount { get; set; }
}
