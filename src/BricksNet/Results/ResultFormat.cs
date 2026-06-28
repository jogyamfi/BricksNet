namespace BricksNet.Results;

/// <summary>Wire format of a query result payload.</summary>
public enum ResultFormat
{
    /// <summary>Apache Arrow IPC stream (primary format).</summary>
    Arrow,

    /// <summary>JSON columnar inline payload.</summary>
    JsonColumnar,

    /// <summary>External links for cloud-based result chunks.</summary>
    CloudFetch,
}
