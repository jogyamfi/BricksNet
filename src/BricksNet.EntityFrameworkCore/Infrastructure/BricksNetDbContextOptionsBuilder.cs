using BricksNet.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BricksNet.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Allows BricksNet-specific configuration to be performed on <see cref="DbContextOptions"/>.
/// Obtained from the optional action passed to
/// <see cref="BricksNetDbContextOptionsBuilderExtensions.UseBricksNet(DbContextOptionsBuilder, string?, Action{BricksNetDbContextOptionsBuilder}?)"/>.
/// </summary>
public class BricksNetDbContextOptionsBuilder
    : RelationalDbContextOptionsBuilder<BricksNetDbContextOptionsBuilder, BricksNetOptionsExtension>
{
    /// <summary>Initialises a new instance.</summary>
    /// <param name="optionsBuilder">The core options builder.</param>
    public BricksNetDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        : base(optionsBuilder)
    {
    }

    /// <summary>
    /// Whether connections keep their Databricks session open while EF closes and re-opens them
    /// between operations. On by default, which avoids starting a new session for every query;
    /// sessions are released when the context is disposed.
    /// </summary>
    /// <param name="keepSessionAlive"><see langword="false"/> to open a new session on every connection open.</param>
    /// <returns>The same builder instance so that calls can be chained.</returns>
    public virtual BricksNetDbContextOptionsBuilder KeepSessionAlive(bool keepSessionAlive = true) =>
        WithOption(extension => extension.WithKeepSessionAlive(keepSessionAlive));

    /// <summary>
    /// Retries operations that fail transiently: throttling or unavailability (HTTP 429/503) and Delta
    /// concurrent-transaction conflicts. Queries are retried as a whole; <c>SaveChanges</c>
    /// retries only the failed statement, because earlier statements have already committed.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retries. Defaults to 6.</param>
    /// <param name="maxRetryDelay">The longest delay between retries. Defaults to 30 seconds.</param>
    /// <returns>The same builder instance so that calls can be chained.</returns>
    public virtual BricksNetDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount = 6, TimeSpan? maxRetryDelay = null)
    {
        var delay = maxRetryDelay ?? TimeSpan.FromSeconds(30);
        WithOption(extension => extension.WithRetryOnFailure(maxRetryCount, delay));
        return ExecutionStrategy(dependencies => new BricksNetRetryingExecutionStrategy(dependencies, maxRetryCount, delay));
    }
}
