namespace BricksNet.Contracts.Options;

/// <summary>Observability settings for diagnostic events and logging.</summary>
/// <remarks>
/// Spans are emitted on the <see cref="BricksNet.Telemetry.BricksNetActivitySource.SourceName"/>
/// activity source and cost nothing unless a listener subscribes to it.
/// </remarks>
public sealed record TelemetryOptions
{
    /// <summary>
    /// When <see langword="true"/>, query text is recorded on statement spans. Keep
    /// <see langword="false"/> in production.
    /// </summary>
    public bool SensitiveDataLoggingEnabled { get; init; }
}
