namespace BricksNet.Contracts.Options;

/// <summary>Backend-level options passed to <c>IBackend.OpenSessionAsync</c>.</summary>
public sealed record BackendSessionOptions
{
    /// <summary>Default catalog for the session.</summary>
    public string? Catalog { get; init; }

    /// <summary>Default schema for the session.</summary>
    public string? Schema { get; init; }

    /// <summary>Statement timeout in seconds. <c>0</c> means no timeout.</summary>
    public int QueryTimeoutSeconds { get; init; }

    /// <summary>Maximum rows returned per fetch.</summary>
    public int MaxRows { get; init; } = 10_000;

    /// <summary>Query tags forwarded to the backend.</summary>
    public IReadOnlyDictionary<string, string>? QueryTags { get; init; }
}
