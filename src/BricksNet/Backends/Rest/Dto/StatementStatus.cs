namespace BricksNet.Backends.Rest.Dto;

/// <summary>Lifecycle status block of an <see cref="ExecuteStatementResponse"/>.</summary>
internal sealed class StatementStatus
{
    /// <summary>Current state of the statement.</summary>
    public StatementState State { get; set; }

    /// <summary>Populated when <see cref="State"/> is <see cref="StatementState.Failed"/>.</summary>
    public ServiceError? Error { get; set; }
}

/// <summary>Error detail returned by the SQL Statement Execution API.</summary>
internal sealed class ServiceError
{
    /// <summary>Machine-readable error code (for example <c>BAD_REQUEST</c>).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Human-readable error message.</summary>
    public string? Message { get; set; }
}
