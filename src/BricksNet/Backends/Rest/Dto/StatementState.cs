using System.Text.Json.Serialization;

namespace BricksNet.Backends.Rest.Dto;

/// <summary>
/// Lifecycle state of a statement as reported by the SQL Statement Execution API.
/// Deserialised from the <c>status.state</c> field. Matching is case-insensitive,
/// so the upper-case wire values (<c>PENDING</c>, <c>RUNNING</c>, …) map onto these members.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementState>))]
internal enum StatementState
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
    Canceled,

    /// <summary>Statement handle has been closed.</summary>
    Closed,
}
