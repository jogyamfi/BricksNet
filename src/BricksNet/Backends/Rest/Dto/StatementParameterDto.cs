namespace BricksNet.Backends.Rest.Dto;

/// <summary>
/// A named statement parameter sent in <see cref="ExecuteStatementRequest.Parameters"/>.
/// Mirrors the <c>parameters[*]</c> objects of the SQL Statement Execution API.
/// </summary>
internal sealed class StatementParameterDto
{
    /// <summary>Parameter name, without the leading colon used in the SQL text.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>String-encoded parameter value, or <see langword="null"/> for SQL <c>NULL</c>.</summary>
    public string? Value { get; set; }

    /// <summary>Optional SQL type of the parameter (for example <c>STRING</c> or <c>INT</c>).</summary>
    public string? Type { get; set; }
}
