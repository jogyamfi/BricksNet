namespace BricksNet.Contracts.Options;

/// <summary>Per-session defaults applied to every statement unless overridden.</summary>
public record SessionOptions
{
    /// <summary>Default catalog for unqualified object references.</summary>
    public string? Catalog { get; init; }

    /// <summary>Default schema for unqualified object references.</summary>
    public string? Schema { get; init; }

    /// <summary>Statement timeout in seconds. <c>0</c> means no timeout.</summary>
    public int QueryTimeoutSeconds { get; init; }

    /// <summary>Maximum rows per fetch chunk.</summary>
    public int MaxRows { get; init; } = 10_000;

    /// <summary>Key-value query tags forwarded to the warehouse for observability.</summary>
    public IReadOnlyDictionary<string, string>? QueryTags { get; init; }
}
