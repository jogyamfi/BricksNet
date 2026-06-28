namespace BricksNet.Contracts.Options;

/// <summary>A named, typed parameter for a parameterised statement.</summary>
public sealed record BricksNetParameter(
    string Name,
    object? Value,
    string? TypeHint = null);
