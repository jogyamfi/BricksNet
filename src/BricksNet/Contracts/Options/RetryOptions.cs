namespace BricksNet.Contracts.Options;

/// <summary>Retry policy applied to transient HTTP failures (429, 503).</summary>
public sealed record RetryOptions
{
    /// <summary>Maximum number of attempts (first try + retries).</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Total retry budget in milliseconds.</summary>
    public int TimeoutMs { get; init; } = 120_000;

    /// <summary>Minimum per-attempt jitter delay in milliseconds.</summary>
    public int DelayMinMs { get; init; } = 200;

    /// <summary>Maximum per-attempt jitter delay cap in milliseconds.</summary>
    public int DelayMaxMs { get; init; } = 30_000;
}
