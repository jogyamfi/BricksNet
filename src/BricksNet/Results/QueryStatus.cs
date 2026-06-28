namespace BricksNet.Results;

/// <summary>Lifecycle state of a submitted statement.</summary>
public enum QueryStatus
{
    /// <summary>Statement is queued but not yet executing.</summary>
    Pending,

    /// <summary>Statement is actively executing.</summary>
    Running,

    /// <summary>Statement completed successfully.</summary>
    Succeeded,

    /// <summary>Statement terminated with an error.</summary>
    Failed,

    /// <summary>Statement was cancelled by the client.</summary>
    Cancelled,

    /// <summary>Operation handle has been closed.</summary>
    Closed,
}
