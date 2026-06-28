namespace BricksNet.Contracts.Options;

/// <summary>Options for a single statement execution. Extends session-level defaults.</summary>
public sealed record StatementOptions : SessionOptions
{
    /// <summary>Bound parameters for a parameterised statement.</summary>
    public IReadOnlyList<BricksNetParameter>? Parameters { get; init; }
}
