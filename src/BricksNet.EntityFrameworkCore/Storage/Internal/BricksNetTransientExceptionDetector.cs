using BricksNet.AdoNet;

namespace BricksNet.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Decides whether a failure is worth retrying: Databricks throttling or unavailability (HTTP
/// 429/503) and Delta concurrent-transaction conflicts, as reported by
/// <see cref="BricksNetException.IsTransient"/>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public static class BricksNetTransientExceptionDetector
{
    /// <summary>
    /// Marks failures that must not be retried as a whole. EF's execution strategy unwraps a
    /// <c>DbUpdateException</c> before deciding whether to retry, so a <c>SaveChanges</c> failure is
    /// recognised by this marker on every exception in its chain.
    /// </summary>
    private const string NoRetryKey = "BricksNet.NoRetry";

    /// <summary>
    /// Marks <paramref name="exception"/> and its inner exceptions so the execution strategy does not
    /// retry them: a failed <c>SaveChanges</c> may already have committed earlier statements.
    /// </summary>
    public static void MarkNotRetryable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            current.Data[NoRetryKey] = true;
        }
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="exception"/> or one of its inner exceptions is transient.</summary>
    public static bool ShouldRetryOn(Exception? exception)
    {
        if (exception?.Data.Contains(NoRetryKey) == true)
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is BricksNetException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The delay before retry number <paramref name="attempt"/> (starting at 1): exponential backoff
    /// with jitter, capped at <paramref name="maxDelay"/>.
    /// </summary>
    public static TimeSpan GetDelay(int attempt, TimeSpan maxDelay)
    {
        var exponential = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1) * (0.8 + (Random.Shared.NextDouble() * 0.4)));
        return exponential < maxDelay ? exponential : maxDelay;
    }
}
