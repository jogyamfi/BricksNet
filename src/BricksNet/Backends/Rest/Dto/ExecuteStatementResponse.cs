namespace BricksNet.Backends.Rest.Dto;

/// <summary>
/// Response body shared by <c>POST /api/2.0/sql/statements</c> and
/// <c>GET /api/2.0/sql/statements/{id}</c>.
/// </summary>
internal sealed class ExecuteStatementResponse
{
    /// <summary>Server-assigned statement identifier used by all follow-up calls.</summary>
    public string StatementId { get; set; } = string.Empty;

    /// <summary>Current lifecycle status of the statement.</summary>
    public StatementStatus Status { get; set; } = new();

    /// <summary>Result schema and chunk index. Present once the statement has produced results.</summary>
    public ResultManifest? Manifest { get; set; }

    /// <summary>Embedded first result chunk, present for direct (non-polled) results.</summary>
    public ResultData? Result { get; set; }
}
