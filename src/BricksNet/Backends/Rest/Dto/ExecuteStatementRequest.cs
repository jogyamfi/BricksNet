namespace BricksNet.Backends.Rest.Dto;

/// <summary>
/// Request body for <c>POST /api/2.0/sql/statements</c>. Property names are serialised
/// to <c>snake_case</c> by the shared <see cref="SqlExecJsonContext"/>; <see langword="null"/>
/// properties are omitted.
/// </summary>
internal sealed class ExecuteStatementRequest
{
    /// <summary>Identifier of the SQL warehouse that runs the statement.</summary>
    public string WarehouseId { get; set; } = string.Empty;

    /// <summary>The SQL text to execute.</summary>
    public string Statement { get; set; } = string.Empty;

    /// <summary>Default catalog applied for the duration of the statement.</summary>
    public string? Catalog { get; set; }

    /// <summary>Default schema applied for the duration of the statement.</summary>
    public string? Schema { get; set; }

    /// <summary>Named parameters referenced by the statement text.</summary>
    public IReadOnlyList<StatementParameterDto>? Parameters { get; set; }

    /// <summary>
    /// How long the server waits synchronously for completion before returning, expressed
    /// as <c>"Ns"</c>. Valid values are <c>"0s"</c> or <c>"5s"</c>–<c>"50s"</c>.
    /// </summary>
    public string? WaitTimeout { get; set; }

    /// <summary>Behaviour when <see cref="WaitTimeout"/> elapses before completion (<c>CONTINUE</c> or <c>CANCEL</c>).</summary>
    public string? OnWaitTimeout { get; set; }

    /// <summary>Result delivery disposition (<c>INLINE</c> or <c>EXTERNAL_LINKS</c>).</summary>
    public string? Disposition { get; set; }

    /// <summary>Result serialisation format (<c>JSON_ARRAY</c>, <c>ARROW_STREAM</c> or <c>CSV</c>).</summary>
    public string? Format { get; set; }

    /// <summary>Maximum number of rows to return, or <see langword="null"/> for no limit.</summary>
    public long? RowLimit { get; set; }

    /// <summary>Maximum number of bytes to return, or <see langword="null"/> for no limit.</summary>
    public long? ByteLimit { get; set; }
}
