namespace BricksNet.Contracts.Options;

/// <summary>Observability settings for diagnostic events and logging.</summary>
public sealed record TelemetryOptions
{
    /// <summary>Whether telemetry emission is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Name registered with <c>DiagnosticListener</c> / <c>ActivitySource</c>.</summary>
    public string DiagnosticListenerName { get; init; } = "BricksNet";

    /// <summary>
    /// When <see langword="true"/>, query text and parameter values may appear in
    /// diagnostic events. Keep <see langword="false"/> in production.
    /// </summary>
    public bool SensitiveDataLoggingEnabled { get; init; }
}
