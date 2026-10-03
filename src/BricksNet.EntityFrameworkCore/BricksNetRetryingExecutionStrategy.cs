using BricksNet.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore;

/// <summary>
/// An execution strategy that retries queries (and operations you run through it) on transient Databricks
/// failures: throttling or unavailability (HTTP 429/503) and Delta concurrent-transaction conflicts.
/// Enable it with <c>UseBricksNet(..., o => o.EnableRetryOnFailure())</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>SaveChanges</c> failures are not retried here. Databricks has no multi-statement transactions,
/// so re-running a whole <c>SaveChanges</c> could repeat statements that already committed. The
/// provider instead retries each failed statement individually.
/// </para>
/// <para>
/// EF does not run <c>ExecuteSql</c> through the execution strategy. To retry raw SQL, wrap it:
/// <c>await context.Database.CreateExecutionStrategy().ExecuteAsync(() =&gt; context.Database.ExecuteSqlAsync(...))</c>.
/// </para>
/// </remarks>
public class BricksNetRetryingExecutionStrategy : ExecutionStrategy
{
    /// <summary>Initialises a new instance.</summary>
    /// <param name="dependencies">Parameter object containing service dependencies.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retries.</param>
    public BricksNetRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount, TimeSpan maxRetryDelay)
        : base(dependencies, maxRetryCount, maxRetryDelay)
    {
    }

    /// <inheritdoc />
    protected override bool ShouldRetryOn(Exception exception) =>
        BricksNetTransientExceptionDetector.ShouldRetryOn(exception);
}
