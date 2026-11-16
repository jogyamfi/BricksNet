namespace BricksNet.Contracts.Options;

/// <summary>Options controlling a single backend fetch call.</summary>
internal sealed record FetchOptions
{
    /// <summary>Maximum number of rows to retrieve in this fetch.</summary>
    public int MaxRows { get; init; } = 10_000;

    /// <summary>Cancellation token for the fetch operation.</summary>
    public CancellationToken CancellationToken { get; init; }
}
